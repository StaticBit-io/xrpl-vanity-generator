// XRPL vanity — CUDA kernels. Device-only source compiled to PTX by gpu/build-ptx.py.
// Every section below is verified by tests/XrplVanity.Tests/Gpu before the next is built on it.

typedef unsigned char u8;
typedef unsigned int u32;
typedef unsigned long long u64;

// ---------------------------------------------------------------------------
// M0: infrastructure probe
// ---------------------------------------------------------------------------
extern "C" __global__ void k_add_arrays(const u32* a, const u32* b, u32* out, int n)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n) return;
    out[i] = a[i] + b[i];
}

// ---------------------------------------------------------------------------
// M1: field arithmetic mod p = 2^255 - 19
// Representation: 8 x 32-bit little-endian limbs. Every function below that
// produces a field element returns it CANONICAL (fully reduced, < p).
// ---------------------------------------------------------------------------
struct Fe { u32 v[8]; };

__device__ __constant__ u32 P_LIMBS[8] = {
    0xffffffedu, 0xffffffffu, 0xffffffffu, 0xffffffffu,
    0xffffffffu, 0xffffffffu, 0xffffffffu, 0x7fffffffu,
};
__device__ __constant__ u32 P_MINUS_2_LIMBS[8] = {
    0xffffffebu, 0xffffffffu, 0xffffffffu, 0xffffffffu,
    0xffffffffu, 0xffffffffu, 0xffffffffu, 0x7fffffffu,
};

__device__ __forceinline__ void fe_zero(Fe& r) { for (int i = 0; i < 8; i++) r.v[i] = 0; }
__device__ __forceinline__ void fe_one(Fe& r)  { fe_zero(r); r.v[0] = 1; }
__device__ __forceinline__ void fe_copy(Fe& r, const Fe& a) { for (int i = 0; i < 8; i++) r.v[i] = a.v[i]; }

// If a >= p then a -= p (exactly one conditional subtraction).
__device__ __forceinline__ void fe_cond_sub_p(Fe& a)
{
    u32 t[8];
    u64 borrow = 0;
    for (int i = 0; i < 8; i++) {
        u64 d = (u64)a.v[i] - (u64)P_LIMBS[i] - borrow;
        t[i] = (u32)d;
        borrow = (d >> 63) & 1ull;
    }
    if (!borrow) {
        for (int i = 0; i < 8; i++) a.v[i] = t[i];
    }
}

// Any value < 2^256 (= 2p + 38) becomes canonical after two conditional subtractions.
__device__ __forceinline__ void fe_canon(Fe& a) { fe_cond_sub_p(a); fe_cond_sub_p(a); }

__device__ __forceinline__ void fe_add(Fe& r, const Fe& a, const Fe& b)
{
    u64 c = 0;
    for (int i = 0; i < 8; i++) {
        c += (u64)a.v[i] + (u64)b.v[i];
        r.v[i] = (u32)c;
        c >>= 32;
    }
    // a, b < p  =>  a + b < 2p < 2^256, so no carry escapes; one subtraction canonicalises.
    fe_cond_sub_p(r);
}

__device__ __forceinline__ void fe_sub(Fe& r, const Fe& a, const Fe& b)
{
    u32 t[8];
    u64 borrow = 0;
    for (int i = 0; i < 8; i++) {
        u64 d = (u64)a.v[i] - (u64)b.v[i] - borrow;
        t[i] = (u32)d;
        borrow = (d >> 63) & 1ull;
    }
    if (borrow) {
        u64 c = 0;
        for (int i = 0; i < 8; i++) {
            c += (u64)t[i] + (u64)P_LIMBS[i];
            t[i] = (u32)c;
            c >>= 32;
        }
    }
    for (int i = 0; i < 8; i++) r.v[i] = t[i];
}

// r = a * b mod p. Computes into a local first, so r may alias a or b.
__device__ void fe_mul(Fe& r, const Fe& a, const Fe& b)
{
    u64 t[16];
    for (int i = 0; i < 16; i++) t[i] = 0;
    for (int i = 0; i < 8; i++) {
        u64 carry = 0;
        for (int j = 0; j < 8; j++) {
            u64 p = (u64)a.v[i] * (u64)b.v[j] + t[i + j] + carry;
            t[i + j] = p & 0xffffffffull;
            carry = p >> 32;
        }
        t[i + 8] += carry;
    }
    // Fold the high 256 bits: 2^256 == 38 (mod p).
    u32 s[9];
    u64 carry = 0;
    for (int i = 0; i < 8; i++) {
        u64 acc = t[i] + 38ull * t[i + 8] + carry;
        s[i] = (u32)acc;
        carry = acc >> 32;
    }
    s[8] = (u32)carry;
    Fe o;
    u64 add = 38ull * (u64)s[8];
    for (int i = 0; i < 8; i++) {
        u64 acc = (u64)s[i] + add;
        o.v[i] = (u32)acc;
        add = acc >> 32;
    }
    u64 add2 = 38ull * add;
    for (int i = 0; i < 8 && add2; i++) {
        u64 acc = (u64)o.v[i] + add2;
        o.v[i] = (u32)acc;
        add2 = acc >> 32;
    }
    fe_canon(o);
    fe_copy(r, o);
}

__device__ __forceinline__ void fe_sqr(Fe& r, const Fe& a) { fe_mul(r, a, a); }

// r = base^exp, exp given as 8 little-endian limbs; square-and-multiply, MSB first.
__device__ void fe_pow(Fe& r, const Fe& base, const u32* exp)
{
    Fe acc;
    fe_one(acc);
    for (int i = 255; i >= 0; i--) {
        fe_sqr(acc, acc);
        if ((exp[i >> 5] >> (i & 31)) & 1u) fe_mul(acc, acc, base);
    }
    fe_copy(r, acc);
}

// r = a^-1 = a^(p-2) (Fermat). a must be non-zero.
__device__ void fe_inv(Fe& r, const Fe& a) { fe_pow(r, a, P_MINUS_2_LIMBS); }

__device__ __forceinline__ void fe_from_bytes(Fe& r, const u8* in32)
{
    for (int i = 0; i < 8; i++) {
        r.v[i] = (u32)in32[4 * i] | ((u32)in32[4 * i + 1] << 8) | ((u32)in32[4 * i + 2] << 16) | ((u32)in32[4 * i + 3] << 24);
    }
    fe_canon(r);
}

__device__ __forceinline__ void fe_to_bytes(u8* out32, const Fe& a)
{
    for (int i = 0; i < 8; i++) {
        out32[4 * i]     = (u8)(a.v[i]);
        out32[4 * i + 1] = (u8)(a.v[i] >> 8);
        out32[4 * i + 2] = (u8)(a.v[i] >> 16);
        out32[4 * i + 3] = (u8)(a.v[i] >> 24);
    }
}

// Test entry: element-wise field op over n elements. op: 0 add, 1 sub, 2 mul, 3 sqr, 4 inv, 5 canon, 6 from_bytes.
extern "C" __global__ void k_fe_op(const u32* a, const u32* b, u32* out, int n, int op)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n) return;
    Fe x, y, r;
    for (int k = 0; k < 8; k++) { x.v[k] = a[i * 8 + k]; y.v[k] = b[i * 8 + k]; }
    switch (op) {
        case 0: fe_add(r, x, y); break;
        case 1: fe_sub(r, x, y); break;
        case 2: fe_mul(r, x, y); break;
        case 3: fe_sqr(r, x); break;
        case 4: fe_inv(r, x); break;
        case 6: {
            u8 raw[32];
            for (int k = 0; k < 8; k++) {
                raw[4 * k]     = (u8)(x.v[k]);
                raw[4 * k + 1] = (u8)(x.v[k] >> 8);
                raw[4 * k + 2] = (u8)(x.v[k] >> 16);
                raw[4 * k + 3] = (u8)(x.v[k] >> 24);
            }
            fe_from_bytes(r, raw);
            break;
        }
        default: fe_copy(r, x); fe_canon(r); break;
    }
    for (int k = 0; k < 8; k++) out[i * 8 + k] = r.v[k];
}

// ---------------------------------------------------------------------------
// M2: Ed25519 points in extended twisted-Edwards coordinates (X, Y, Z, T), a = -1.
// Formulas: RFC 8032 s5.1.4 (dbl-2008-hwcd, add-2008-hwcd-3 with 2d).
// Constants verified against RFC 8032 s7.1 TEST 1-3 with an independent reference.
// ---------------------------------------------------------------------------
struct Pt { Fe X, Y, Z, T; };

__device__ __constant__ u32 D2_LIMBS[8] = {
    0x26b2f159u, 0xebd69b94u, 0x8283b156u, 0x00e0149au, 0xeef3d130u, 0x198e80f2u, 0x56dffce7u, 0x2406d9dcu,
};
__device__ __constant__ u32 BX_LIMBS[8] = {
    0x8f25d51au, 0xc9562d60u, 0x9525a7b2u, 0x692cc760u, 0xfdd6dc5cu, 0xc0a4e231u, 0xcd6e53feu, 0x216936d3u,
};
__device__ __constant__ u32 BY_LIMBS[8] = {
    0x66666658u, 0x66666666u, 0x66666666u, 0x66666666u, 0x66666666u, 0x66666666u, 0x66666666u, 0x66666666u,
};
__device__ __constant__ u32 BT_LIMBS[8] = {
    0xa5b7dda3u, 0x6dde8ab3u, 0x775152f5u, 0x20f09f80u, 0x64abe37du, 0x66ea4e8eu, 0xd78b7665u, 0x67875f0fu,
};

__device__ __forceinline__ void fe_load_const(Fe& r, const u32* limbs) { for (int i = 0; i < 8; i++) r.v[i] = limbs[i]; }

__device__ __forceinline__ void pt_identity(Pt& r) { fe_zero(r.X); fe_one(r.Y); fe_one(r.Z); fe_zero(r.T); }
__device__ __forceinline__ void pt_base(Pt& r)
{
    fe_load_const(r.X, BX_LIMBS);
    fe_load_const(r.Y, BY_LIMBS);
    fe_one(r.Z);
    fe_load_const(r.T, BT_LIMBS);
}

// r = p + q. All reads of p and q complete before r is written, so r may alias p or q.
__device__ void pt_add(Pt& r, const Pt& p, const Pt& q)
{
    Fe A, B, C, D, E, F, G, H, t1, t2, d2;
    fe_sub(t1, p.Y, p.X); fe_sub(t2, q.Y, q.X); fe_mul(A, t1, t2);
    fe_add(t1, p.Y, p.X); fe_add(t2, q.Y, q.X); fe_mul(B, t1, t2);
    fe_load_const(d2, D2_LIMBS); fe_mul(t1, p.T, d2); fe_mul(C, t1, q.T);
    fe_add(t1, p.Z, p.Z); fe_mul(D, t1, q.Z);
    fe_sub(E, B, A); fe_sub(F, D, C); fe_add(G, D, C); fe_add(H, B, A);
    fe_mul(r.X, E, F); fe_mul(r.Y, G, H); fe_mul(r.Z, F, G); fe_mul(r.T, E, H);
}

// r = 2p. Same aliasing guarantee as pt_add.
__device__ void pt_dbl(Pt& r, const Pt& p)
{
    Fe A, B, C, H, E, G, F, t;
    fe_sqr(A, p.X); fe_sqr(B, p.Y); fe_sqr(t, p.Z); fe_add(C, t, t);
    fe_add(H, A, B); fe_add(t, p.X, p.Y); fe_sqr(t, t); fe_sub(E, H, t);
    fe_sub(G, A, B); fe_add(F, C, G);
    fe_mul(r.X, E, F); fe_mul(r.Y, G, H); fe_mul(r.Z, F, G); fe_mul(r.T, E, H);
}

// r = scalar * B, scalar as 32 little-endian bytes (already clamped). Naive double-and-add.
__device__ void scalarmult_base(Pt& r, const u8* scalar32)
{
    Pt R, B;
    pt_identity(R);
    pt_base(B);
    for (int i = 255; i >= 0; i--) {
        pt_dbl(R, R);
        if ((scalar32[i >> 3] >> (i & 7)) & 1u) pt_add(R, R, B);
    }
    r = R;
}

// RFC 8032 s5.1.2: encode y little-endian, sign bit of x in the top bit of the last byte.
__device__ void pt_compress(u8* out32, const Pt& p)
{
    Fe zi, x, y;
    fe_inv(zi, p.Z);
    fe_mul(x, p.X, zi);
    fe_mul(y, p.Y, zi);
    fe_to_bytes(out32, y);
    out32[31] |= (u8)((x.v[0] & 1u) << 7);
}

// Test entry: n clamped scalars (32 bytes each) -> n compressed public keys (32 bytes each).
extern "C" __global__ void k_scalarmult_base(const u8* scalars, u8* pubs, int n)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n) return;
    Pt P;
    scalarmult_base(P, scalars + i * 32);
    pt_compress(pubs + i * 32, P);
}
