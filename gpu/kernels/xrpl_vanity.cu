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

// ---------------------------------------------------------------------------
// M3: hashes and base58. Constant tables generated from the standards and verified
// against hashlib / known vectors before being pasted here.
// ---------------------------------------------------------------------------
__device__ __constant__ u64 K512[80] = {
    0x428a2f98d728ae22ull, 0x7137449123ef65cdull, 0xb5c0fbcfec4d3b2full, 0xe9b5dba58189dbbcull,
    0x3956c25bf348b538ull, 0x59f111f1b605d019ull, 0x923f82a4af194f9bull, 0xab1c5ed5da6d8118ull,
    0xd807aa98a3030242ull, 0x12835b0145706fbeull, 0x243185be4ee4b28cull, 0x550c7dc3d5ffb4e2ull,
    0x72be5d74f27b896full, 0x80deb1fe3b1696b1ull, 0x9bdc06a725c71235ull, 0xc19bf174cf692694ull,
    0xe49b69c19ef14ad2ull, 0xefbe4786384f25e3ull, 0x0fc19dc68b8cd5b5ull, 0x240ca1cc77ac9c65ull,
    0x2de92c6f592b0275ull, 0x4a7484aa6ea6e483ull, 0x5cb0a9dcbd41fbd4ull, 0x76f988da831153b5ull,
    0x983e5152ee66dfabull, 0xa831c66d2db43210ull, 0xb00327c898fb213full, 0xbf597fc7beef0ee4ull,
    0xc6e00bf33da88fc2ull, 0xd5a79147930aa725ull, 0x06ca6351e003826full, 0x142929670a0e6e70ull,
    0x27b70a8546d22ffcull, 0x2e1b21385c26c926ull, 0x4d2c6dfc5ac42aedull, 0x53380d139d95b3dfull,
    0x650a73548baf63deull, 0x766a0abb3c77b2a8ull, 0x81c2c92e47edaee6ull, 0x92722c851482353bull,
    0xa2bfe8a14cf10364ull, 0xa81a664bbc423001ull, 0xc24b8b70d0f89791ull, 0xc76c51a30654be30ull,
    0xd192e819d6ef5218ull, 0xd69906245565a910ull, 0xf40e35855771202aull, 0x106aa07032bbd1b8ull,
    0x19a4c116b8d2d0c8ull, 0x1e376c085141ab53ull, 0x2748774cdf8eeb99ull, 0x34b0bcb5e19b48a8ull,
    0x391c0cb3c5c95a63ull, 0x4ed8aa4ae3418acbull, 0x5b9cca4f7763e373ull, 0x682e6ff3d6b2b8a3ull,
    0x748f82ee5defb2fcull, 0x78a5636f43172f60ull, 0x84c87814a1f0ab72ull, 0x8cc702081a6439ecull,
    0x90befffa23631e28ull, 0xa4506cebde82bde9ull, 0xbef9a3f7b2c67915ull, 0xc67178f2e372532bull,
    0xca273eceea26619cull, 0xd186b8c721c0c207ull, 0xeada7dd6cde0eb1eull, 0xf57d4f7fee6ed178ull,
    0x06f067aa72176fbaull, 0x0a637dc5a2c898a6ull, 0x113f9804bef90daeull, 0x1b710b35131c471bull,
    0x28db77f523047d84ull, 0x32caab7b40c72493ull, 0x3c9ebe0a15c9bebcull, 0x431d67c49c100d4cull,
    0x4cc5d4becb3e42b6ull, 0x597f299cfc657e2aull, 0x5fcb6fab3ad6faecull, 0x6c44198c4a475817ull,
};

__device__ __constant__ u32 K256[64] = {
    0x428a2f98u, 0x71374491u, 0xb5c0fbcfu, 0xe9b5dba5u, 0x3956c25bu, 0x59f111f1u, 0x923f82a4u, 0xab1c5ed5u,
    0xd807aa98u, 0x12835b01u, 0x243185beu, 0x550c7dc3u, 0x72be5d74u, 0x80deb1feu, 0x9bdc06a7u, 0xc19bf174u,
    0xe49b69c1u, 0xefbe4786u, 0x0fc19dc6u, 0x240ca1ccu, 0x2de92c6fu, 0x4a7484aau, 0x5cb0a9dcu, 0x76f988dau,
    0x983e5152u, 0xa831c66du, 0xb00327c8u, 0xbf597fc7u, 0xc6e00bf3u, 0xd5a79147u, 0x06ca6351u, 0x14292967u,
    0x27b70a85u, 0x2e1b2138u, 0x4d2c6dfcu, 0x53380d13u, 0x650a7354u, 0x766a0abbu, 0x81c2c92eu, 0x92722c85u,
    0xa2bfe8a1u, 0xa81a664bu, 0xc24b8b70u, 0xc76c51a3u, 0xd192e819u, 0xd6990624u, 0xf40e3585u, 0x106aa070u,
    0x19a4c116u, 0x1e376c08u, 0x2748774cu, 0x34b0bcb5u, 0x391c0cb3u, 0x4ed8aa4au, 0x5b9cca4fu, 0x682e6ff3u,
    0x748f82eeu, 0x78a5636fu, 0x84c87814u, 0x8cc70208u, 0x90befffau, 0xa4506cebu, 0xbef9a3f7u, 0xc67178f2u,
};

__device__ __forceinline__ u64 rotr64(u64 x, int n) { return (x >> n) | (x << (64 - n)); }
__device__ __forceinline__ u32 rotr32(u32 x, int n) { return (x >> n) | (x << (32 - n)); }
__device__ __forceinline__ u32 rotl32(u32 x, int n) { return (x << n) | (x >> (32 - n)); }

// SHA-512 for len <= 239 bytes (at most two 128-byte blocks).
__device__ void sha512(const u8* msg, int len, u8* out64)
{
    u64 h[8] = {
        0x6a09e667f3bcc908ull, 0xbb67ae8584caa73bull, 0x3c6ef372fe94f82bull, 0xa54ff53a5f1d36f1ull,
        0x510e527fade682d1ull, 0x9b05688c2b3e6c1full, 0x1f83d9abfb41bd6bull, 0x5be0cd19137e2179ull,
    };
    u8 buf[256];
    int i;
    for (i = 0; i < len; i++) buf[i] = msg[i];
    buf[len] = 0x80;
    int total = ((len + 1 + 16) + 127) / 128 * 128;
    for (i = len + 1; i < total; i++) buf[i] = 0;
    u64 bits = (u64)len * 8ull;
    for (i = 0; i < 8; i++) buf[total - 1 - i] = (u8)(bits >> (8 * i));

    for (int off = 0; off < total; off += 128) {
        u64 w[80];
        for (i = 0; i < 16; i++) {
            u64 v = 0;
            for (int j = 0; j < 8; j++) v = (v << 8) | buf[off + i * 8 + j];
            w[i] = v;
        }
        for (i = 16; i < 80; i++) {
            u64 s0 = rotr64(w[i - 15], 1) ^ rotr64(w[i - 15], 8) ^ (w[i - 15] >> 7);
            u64 s1 = rotr64(w[i - 2], 19) ^ rotr64(w[i - 2], 61) ^ (w[i - 2] >> 6);
            w[i] = w[i - 16] + s0 + w[i - 7] + s1;
        }
        u64 a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
        for (i = 0; i < 80; i++) {
            u64 S1 = rotr64(e, 14) ^ rotr64(e, 18) ^ rotr64(e, 41);
            u64 ch = (e & f) ^ (~e & g);
            u64 t1 = hh + S1 + ch + K512[i] + w[i];
            u64 S0 = rotr64(a, 28) ^ rotr64(a, 34) ^ rotr64(a, 39);
            u64 maj = (a & b) ^ (a & c) ^ (b & c);
            u64 t2 = S0 + maj;
            hh = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
        }
        h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e; h[5] += f; h[6] += g; h[7] += hh;
    }
    for (i = 0; i < 8; i++)
        for (int j = 0; j < 8; j++) out64[i * 8 + j] = (u8)(h[i] >> (56 - 8 * j));
}

// SHA-256 for len <= 183 bytes (at most three 64-byte blocks).
__device__ void sha256(const u8* msg, int len, u8* out32)
{
    u32 h[8] = { 0x6a09e667u, 0xbb67ae85u, 0x3c6ef372u, 0xa54ff53au, 0x510e527fu, 0x9b05688cu, 0x1f83d9abu, 0x5be0cd19u };
    u8 buf[192];
    int i;
    for (i = 0; i < len; i++) buf[i] = msg[i];
    buf[len] = 0x80;
    int total = ((len + 1 + 8) + 63) / 64 * 64;
    for (i = len + 1; i < total; i++) buf[i] = 0;
    u64 bits = (u64)len * 8ull;
    for (i = 0; i < 8; i++) buf[total - 1 - i] = (u8)(bits >> (8 * i));

    for (int off = 0; off < total; off += 64) {
        u32 w[64];
        for (i = 0; i < 16; i++) {
            w[i] = ((u32)buf[off + i * 4] << 24) | ((u32)buf[off + i * 4 + 1] << 16) | ((u32)buf[off + i * 4 + 2] << 8) | (u32)buf[off + i * 4 + 3];
        }
        for (i = 16; i < 64; i++) {
            u32 s0 = rotr32(w[i - 15], 7) ^ rotr32(w[i - 15], 18) ^ (w[i - 15] >> 3);
            u32 s1 = rotr32(w[i - 2], 17) ^ rotr32(w[i - 2], 19) ^ (w[i - 2] >> 10);
            w[i] = w[i - 16] + s0 + w[i - 7] + s1;
        }
        u32 a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
        for (i = 0; i < 64; i++) {
            u32 S1 = rotr32(e, 6) ^ rotr32(e, 11) ^ rotr32(e, 25);
            u32 ch = (e & f) ^ (~e & g);
            u32 t1 = hh + S1 + ch + K256[i] + w[i];
            u32 S0 = rotr32(a, 2) ^ rotr32(a, 13) ^ rotr32(a, 22);
            u32 maj = (a & b) ^ (a & c) ^ (b & c);
            u32 t2 = S0 + maj;
            hh = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
        }
        h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e; h[5] += f; h[6] += g; h[7] += hh;
    }
    for (i = 0; i < 8; i++) {
        out32[i * 4] = (u8)(h[i] >> 24); out32[i * 4 + 1] = (u8)(h[i] >> 16);
        out32[i * 4 + 2] = (u8)(h[i] >> 8); out32[i * 4 + 3] = (u8)h[i];
    }
}

__device__ __constant__ u32 RMD_R[80] = {
    0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
    7, 4, 13, 1, 10, 6, 15, 3, 12, 0, 9, 5, 2, 14, 11, 8,
    3, 10, 14, 4, 9, 15, 8, 1, 2, 7, 0, 6, 13, 11, 5, 12,
    1, 9, 11, 10, 0, 8, 12, 4, 13, 3, 7, 15, 14, 5, 6, 2,
    4, 0, 5, 9, 7, 12, 2, 10, 14, 1, 3, 8, 11, 6, 15, 13,
};
__device__ __constant__ u32 RMD_RR[80] = {
    5, 14, 7, 0, 9, 2, 11, 4, 13, 6, 15, 8, 1, 10, 3, 12,
    6, 11, 3, 7, 0, 13, 5, 10, 14, 15, 8, 12, 4, 9, 1, 2,
    15, 5, 1, 3, 7, 14, 6, 9, 11, 8, 12, 2, 10, 0, 4, 13,
    8, 6, 4, 1, 3, 11, 15, 0, 5, 12, 2, 13, 9, 7, 10, 14,
    12, 15, 10, 4, 1, 5, 8, 7, 6, 2, 13, 14, 0, 3, 9, 11,
};
__device__ __constant__ u32 RMD_S[80] = {
    11, 14, 15, 12, 5, 8, 7, 9, 11, 13, 14, 15, 6, 7, 9, 8,
    7, 6, 8, 13, 11, 9, 7, 15, 7, 12, 15, 9, 11, 7, 13, 12,
    11, 13, 6, 7, 14, 9, 13, 15, 14, 8, 13, 6, 5, 12, 7, 5,
    11, 12, 14, 15, 14, 15, 9, 8, 9, 14, 5, 6, 8, 6, 5, 12,
    9, 15, 5, 11, 6, 8, 13, 12, 5, 12, 13, 14, 11, 8, 5, 6,
};
__device__ __constant__ u32 RMD_SS[80] = {
    8, 9, 9, 11, 13, 15, 15, 5, 7, 7, 8, 11, 14, 14, 12, 6,
    9, 13, 15, 7, 12, 8, 9, 11, 7, 7, 12, 7, 6, 15, 13, 11,
    9, 7, 15, 11, 8, 6, 6, 14, 12, 13, 5, 14, 13, 13, 7, 5,
    15, 5, 8, 11, 14, 14, 6, 14, 6, 9, 12, 9, 12, 5, 15, 8,
    8, 5, 12, 9, 12, 5, 14, 6, 8, 13, 6, 5, 15, 13, 11, 11,
};
__device__ __constant__ u32 RMD_KL[5] = { 0x00000000u, 0x5A827999u, 0x6ED9EBA1u, 0x8F1BBCDCu, 0xA953FD4Eu };
__device__ __constant__ u32 RMD_KR[5] = { 0x50A28BE6u, 0x5C4DD124u, 0x6D703EF3u, 0x7A6D76E9u, 0x00000000u };

__device__ __forceinline__ u32 rmd_f(int j, u32 x, u32 y, u32 z)
{
    if (j < 16) return x ^ y ^ z;
    if (j < 32) return (x & y) | (~x & z);
    if (j < 48) return (x | ~y) ^ z;
    if (j < 64) return (x & z) | (y & ~z);
    return x ^ (y | ~z);
}

// RIPEMD-160 for len <= 119 bytes (at most two 64-byte blocks). Little-endian words and length.
__device__ void ripemd160(const u8* msg, int len, u8* out20)
{
    u32 h[5] = { 0x67452301u, 0xEFCDAB89u, 0x98BADCFEu, 0x10325476u, 0xC3D2E1F0u };
    u8 buf[128];
    int i;
    for (i = 0; i < len; i++) buf[i] = msg[i];
    buf[len] = 0x80;
    int total = ((len + 1 + 8) + 63) / 64 * 64;
    for (i = len + 1; i < total; i++) buf[i] = 0;
    u64 bits = (u64)len * 8ull;
    for (i = 0; i < 8; i++) buf[total - 8 + i] = (u8)(bits >> (8 * i));

    for (int off = 0; off < total; off += 64) {
        u32 X[16];
        for (i = 0; i < 16; i++) {
            X[i] = (u32)buf[off + i * 4] | ((u32)buf[off + i * 4 + 1] << 8) | ((u32)buf[off + i * 4 + 2] << 16) | ((u32)buf[off + i * 4 + 3] << 24);
        }
        u32 al = h[0], bl = h[1], cl = h[2], dl = h[3], el = h[4];
        u32 ar = h[0], br = h[1], cr = h[2], dr = h[3], er = h[4];
        for (int j = 0; j < 80; j++) {
            u32 t = rotl32(al + rmd_f(j, bl, cl, dl) + X[RMD_R[j]] + RMD_KL[j / 16], (int)RMD_S[j]) + el;
            al = el; el = dl; dl = rotl32(cl, 10); cl = bl; bl = t;
            t = rotl32(ar + rmd_f(79 - j, br, cr, dr) + X[RMD_RR[j]] + RMD_KR[j / 16], (int)RMD_SS[j]) + er;
            ar = er; er = dr; dr = rotl32(cr, 10); cr = br; br = t;
        }
        u32 t = h[1] + cl + dr;
        h[1] = h[2] + dl + er;
        h[2] = h[3] + el + ar;
        h[3] = h[4] + al + br;
        h[4] = h[0] + bl + cr;
        h[0] = t;
    }
    for (i = 0; i < 5; i++) {
        out20[i * 4] = (u8)h[i]; out20[i * 4 + 1] = (u8)(h[i] >> 8);
        out20[i * 4 + 2] = (u8)(h[i] >> 16); out20[i * 4 + 3] = (u8)(h[i] >> 24);
    }
}

__device__ __constant__ char B58_ALPHABET[59] = "rpshnaf39wBUDNEGHJKLM4PQRST7VWXYZ2bcdeCg65jkm8oFqi1tuvAxyz";

// Port of XrplVanity.Core.XrplBase58.Encode. Intended for the 25-byte XRPL AccountID payload
// (a leading 0x00 version byte plus a 20-byte RIPEMD-160 hash and 4-byte checksum), which encodes
// to at most 34 characters. `tmp` and `rev` are sized for that case; callers writing into the
// 36-byte [length][chars...] record used throughout this file (35 char slots) must not pass a
// longer input. Returns the char count.
__device__ int base58_encode(const u8* in, int len, char* out)
{
    u8 tmp[32];
    int i;
    for (i = 0; i < len; i++) tmp[i] = in[i];
    int zeros = 0;
    while (zeros < len && tmp[zeros] == 0) zeros++;
    char rev[48];
    int rl = 0;
    int start = zeros;
    while (start < len) {
        int rem = 0;
        for (i = start; i < len; i++) {
            int cur = (rem << 8) | tmp[i];
            tmp[i] = (u8)(cur / 58);
            rem = cur % 58;
        }
        rev[rl++] = B58_ALPHABET[rem];
        while (start < len && tmp[start] == 0) start++;
    }
    int pos = 0;
    for (i = 0; i < zeros; i++) out[pos++] = B58_ALPHABET[0];
    for (i = rl - 1; i >= 0; i--) out[pos++] = rev[i];
    return pos;
}

// Test entries: each thread hashes in[i*inLen .. +inLen]. inLen comes from the host, so each
// guards against overrunning the fixed-size stack buffer inside the corresponding hash function
// (sha512: 256 bytes / 239-byte limit, sha256: 192 bytes / 183-byte limit, ripemd160: 128 bytes /
// 119-byte limit) before it writes the 0x80 padding byte.
extern "C" __global__ void k_sha512(const u8* in, u8* out, int n, int inLen)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n) return;
    if (inLen < 0 || inLen > 239) return;
    sha512(in + i * inLen, inLen, out + i * 64);
}
extern "C" __global__ void k_sha256(const u8* in, u8* out, int n, int inLen)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n) return;
    if (inLen < 0 || inLen > 183) return;
    sha256(in + i * inLen, inLen, out + i * 32);
}
extern "C" __global__ void k_ripemd160(const u8* in, u8* out, int n, int inLen)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n) return;
    if (inLen < 0 || inLen > 119) return;
    ripemd160(in + i * inLen, inLen, out + i * 20);
}
// n x 25-byte payloads -> n x 36 bytes: [length][chars...][zero padding].
extern "C" __global__ void k_base58(const u8* in, u8* out, int n)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n) return;
    char buf[48];
    int len = base58_encode(in + i * 25, 25, buf);
    u8* o = out + i * 36;
    o[0] = (u8)len;
    for (int j = 0; j < 35; j++) o[1 + j] = (j < len) ? (u8)buf[j] : 0;
}

// ---------------------------------------------------------------------------
// M4: full XRPL Ed25519 derivation, entropy16 -> classic address. Step for step the
// same algorithm as XrplVanity.Core.BouncyCastleDeriver.
// ---------------------------------------------------------------------------
__device__ void derive_address(const u8* entropy16, u8* out36)
{
    u8 h1[64];
    sha512(entropy16, 16, h1);                 // seed32 = h1[0..32]  (XRPL)
    u8 h2[64];
    sha512(h1, 32, h2);                        // RFC 8032: scalar = clamp(SHA-512(seed32)[0..32])
    u8 a[32];
    for (int j = 0; j < 32; j++) a[j] = h2[j];
    a[0] &= 248; a[31] &= 127; a[31] |= 64;

    Pt P;
    scalarmult_base(P, a);
    u8 pub[33];
    pub[0] = 0xED;
    pt_compress(pub + 1, P);

    u8 s[32];
    sha256(pub, 33, s);
    u8 payload[25];
    payload[0] = 0x00;                         // AccountID version
    ripemd160(s, 32, payload + 1);
    u8 c1[32], c2[32];
    sha256(payload, 21, c1);
    sha256(c1, 32, c2);
    for (int j = 0; j < 4; j++) payload[21 + j] = c2[j];

    char addr[48];
    int len = base58_encode(payload, 25, addr);
    out36[0] = (u8)len;
    for (int j = 0; j < 35; j++) out36[1 + j] = (j < len) ? (u8)addr[j] : 0;
}

// Verification mode: n x 16-byte entropies -> n x 36-byte address records.
extern "C" __global__ void k_derive_address(const u8* entropy, u8* out, int n)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n) return;
    derive_address(entropy + i * 16, out + i * 36);
}

// Per-launch entropy PRF: out[i] = SHA-512(key32 || LE64(start + i))[0..16].
extern "C" __global__ void k_prf_entropy(const u8* key32, u64 start, u8* out, int n)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n) return;
    u8 m[40];
    for (int j = 0; j < 32; j++) m[j] = key32[j];
    u64 c = start + (u64)i;
    for (int j = 0; j < 8; j++) m[32 + j] = (u8)(c >> (8 * j));
    u8 h[64];
    sha512(m, 40, h);
    for (int j = 0; j < 16; j++) out[i * 16 + j] = h[j];
}

// ---------------------------------------------------------------------------
// M5: device-side matching over host-sorted target groups.
// groups: 4 x u32 per group = { position (0 starts, 1 ends, 2 contains), length,
//         offset into chars, count }. Targets inside a group are sorted ordinal
//         (unsigned byte order) and packed at stride = length.
// The predicate below equals XrplVanity.Core.PatternSet.TryMatch's hit / no-hit result.
// ---------------------------------------------------------------------------
__device__ __forceinline__ int cmp_bytes(const u8* a, const u8* b, int len)
{
    for (int i = 0; i < len; i++) {
        if (a[i] != b[i]) return (a[i] < b[i]) ? -1 : 1;
    }
    return 0;
}

// Binary search for `slice` (exactly `len` bytes) inside one sorted group.
__device__ bool group_has(const u8* chars, u32 offset, u32 count, u32 len, const u8* slice)
{
    int lo = 0;
    int hi = (int)count - 1;
    while (lo <= hi) {
        int mid = (lo + hi) >> 1;
        int c = cmp_bytes(chars + offset + (u32)mid * len, slice, (int)len);
        if (c == 0) return true;
        if (c < 0) lo = mid + 1; else hi = mid - 1;
    }
    return false;
}

// body = the address WITHOUT its leading 'r'; bodyLen = its length.
__device__ bool matches_any(const u8* body, int bodyLen, const u32* groups, int groupCount, const u8* chars)
{
    for (int g = 0; g < groupCount; g++) {
        u32 pos = groups[g * 4];
        u32 len = groups[g * 4 + 1];
        u32 off = groups[g * 4 + 2];
        u32 cnt = groups[g * 4 + 3];
        if ((int)len > bodyLen || cnt == 0 || len == 0) continue;
        if (pos == 0) {
            if (group_has(chars, off, cnt, len, body)) return true;
        } else if (pos == 1) {
            if (group_has(chars, off, cnt, len, body + bodyLen - (int)len)) return true;
        } else {
            for (int s = 0; s + (int)len <= bodyLen; s++) {
                if (group_has(chars, off, cnt, len, body + s)) return true;
            }
        }
    }
    return false;
}

// Test entry: n x 36-byte address records ([len][chars...]) -> flags[i] = 1 on a hit.
extern "C" __global__ void k_match_records(const u8* records, int n, const u32* groups, int groupCount, const u8* chars, u8* flags)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n) return;
    const u8* rec = records + i * 36;
    int len = rec[0];
    flags[i] = (len >= 1 && matches_any(rec + 2, len - 1, groups, groupCount, chars)) ? 1 : 0;
}
