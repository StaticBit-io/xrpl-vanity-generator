# Backend Abstraction and OpenCL Backend — Plan C Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the verified GPU pipeline run on any OpenCL device from the same kernel source, behind a backend abstraction, with every existing correctness anchor re-run on both CUDA and OpenCL and `--backend/--device/--list-devices` in the CLI.

**Architecture:** The kernel becomes one body file in the CUDA / OpenCL C 1.2 intersection plus two shims (`cuda_shim.h`, `opencl_shim.h`); `build-ptx.py` concatenates the CUDA shim + body for NVRTC exactly as before, and the OpenCL backend embeds the OpenCL shim + body as text and compiles it at run time. `IGpuBackend`/`IGpuDevice`/`IGpuBuffer`/`IGpuKernel` sit between the engine/tests and the two implementations; `CudaDevice` adapts the existing `CudaContext`, `OpenClDevice` is a new P/Invoke layer over the ICD loader. Tests are migrated to the abstraction and parameterised over backends, so one test body proves both.

**Tech Stack:** .NET 10, C# latest, CUDA driver API (existing), OpenCL 1.2 API via P/Invoke (`OpenCL.dll` / `libOpenCL.so.1`), NVRTC (existing), xUnit v3 4.0.0.

**Spec:** `docs/2026-09-03-xrpl-vanity-gpu-opencl-design.md` (and the earlier `docs/2026-09-03-xrpl-vanity-gpu-design.md` for the pipeline itself).

## Global Constraints

- `net10.0`; `<Nullable>enable</Nullable>`; `XrplVanity.Gpu` keeps `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`, `<ImplicitUsings>disable</ImplicitUsings>`, no packages beyond `XrplVanity.Core`.
- `dotnet build` must never require CUDA or OpenCL: both are reached via runtime P/Invoke with `NativeLibrary` resolvers; the PTX and the OpenCL source are embedded resources.
- **Kernel body discipline** (validated on NVIDIA OpenCL C 1.2): no C++ references, `typedef struct` types, pointers only; `KERNEL/DEVICE/DEVICE_INLINE/GLOBAL/CONSTANT/CONSTANT_TABLE/GLOBAL_ID()/U64_C()/ATOMIC_INC()` macros from the shim; types only `u8 u32 u64 i8 int char bool`; no `ull`/`ul` literals outside `U64_C`; no `unsigned long`/`long`/`size_t`; device functions take private pointers except the three shared read-only tables (`table`, `groups`, `chars`) which are `GLOBAL const` all the way down; **kernels copy per-thread inputs from `GLOBAL` into private arrays before calling device functions and copy outputs back**; no `#pragma unroll`, no `__launch_bounds__`, no vendor intrinsics.
- The refactored body must reproduce the CUDA results exactly: after Task 1 the entire existing GPU suite (incl. the 1,000,000-input cross-check) passes on CUDA before any OpenCL code exists.
- OpenCL: compile with `-cl-std=CL1.2`; enumerate `CL_DEVICE_TYPE_ALL` on every platform; surface `CL_PROGRAM_BUILD_LOG` in the exception on build failure.
- `--gpu` unchanged (CUDA preferred, then OpenCL); `--backend cuda|opencl`; `--device N`; `--list-devices` (exit 0; exit 2 when nothing is available). Nothing printed that the CPU did not reproduce; exit codes 0/2/130 unchanged.
- Tests: xUnit v3 4.0.0, built-in `Assert`, NO FluentAssertions; **run with `dotnet run`, never `dotnet test`**: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuFieldTests"`; whole suite by omitting `-class`. GPU tests are `[Theory]` over `GpuTestHelper.Backends` (`"cuda"`, `"opencl"`) and skip a backend that is unavailable. Build warning-free.
- Tests that name `Org.BouncyCastle.*` types keep using `extern alias BcCrypto;` (existing csproj alias).
- Commit locally after each task, plain Conventional Commits, NO `Co-Authored-By` / attribution trailers, do NOT push or publish. English artifacts.
- Regenerate the PTX (`python gpu/build-ptx.py`) after every body change and commit it with the body.

## File Structure

```
gpu/kernels/cuda_shim.h                 # CUDA macros/types (Task 1)
gpu/kernels/opencl_shim.h               # OpenCL C 1.2 macros/types (Task 1)
gpu/kernels/xrpl_vanity_body.h          # the single kernel body (Task 1; replaces xrpl_vanity.cu)
gpu/kernels/xrpl_vanity.ptx             # regenerated (Task 1)
gpu/build-ptx.py                        # concatenates cuda_shim.h + body (Task 1)
src/XrplVanity.Gpu/IGpuBackend.cs       # IGpuBackend, IGpuDevice, IGpuKernel, IGpuBuffer, GpuDeviceInfo (Task 2)
src/XrplVanity.Gpu/GpuBackends.cs       # registry: All, FirstAvailableDevice, Find (Task 2; OpenCL added Task 3)
src/XrplVanity.Gpu/Cuda/CudaBackend.cs  # CudaBackend, CudaDevice, CudaKernel, CudaBufferAdapter (Task 2)
src/XrplVanity.Gpu/OpenCl/OpenClNative.cs     # P/Invoke + constants + error names (Task 3)
src/XrplVanity.Gpu/OpenCl/OpenClException.cs  # (Task 3)
src/XrplVanity.Gpu/OpenCl/OpenClBackend.cs    # OpenClBackend, OpenClDevice, OpenClKernel, OpenClBuffer (Task 3)
src/XrplVanity.Gpu/GpuKernels.cs        # + ReadOpenClSource() (Task 3)
src/XrplVanity.Gpu/XrplVanity.Gpu.csproj # + embedded opencl_shim.h, xrpl_vanity_body.h (Task 3)
src/XrplVanity.Gpu/GpuSearchEngine.cs   # on IGpuDevice (Task 5)
src/XrplVanity.Cli/CliOptions.cs        # + Backend, Device, ListDevices (Task 5)
src/XrplVanity.Cli/Program.cs           # selection, --list-devices, RunGpu on IGpuDevice (Task 5)
README.md, docs/                        # (Tasks 5, 6)
tests/XrplVanity.Tests/Gpu/GpuTestHelper.cs   # RequireDevice(backend), Backends member data (Task 2)
tests/XrplVanity.Tests/Gpu/*.cs               # migrated to IGpuDevice + [Theory] (Task 2), OpenCL rows live (Task 4)
```

---

### Task 1: Single-source kernel body with CUDA and OpenCL shims (CUDA no-op gate)

**Files:**
- Create: `gpu/kernels/cuda_shim.h`, `gpu/kernels/opencl_shim.h`, `gpu/kernels/xrpl_vanity_body.h`
- Delete: `gpu/kernels/xrpl_vanity.cu` (its content moves into the body)
- Modify: `gpu/build-ptx.py` (concatenate shim + body); regenerate `gpu/kernels/xrpl_vanity.ptx`
- Test: the existing GPU suite is the gate (no new tests)

**Interfaces:**
- Consumes: the current `xrpl_vanity.cu` (830 lines).
- Produces: the body with these EXACT device-function signatures (names and semantics unchanged; kernel names, parameter lists and record layouts unchanged as seen from the host):

```c
DEVICE_INLINE void fe_zero(Fe* r);
DEVICE_INLINE void fe_one(Fe* r);
DEVICE_INLINE void fe_copy(Fe* r, const Fe* a);
DEVICE_INLINE void fe_cond_sub_p(Fe* a);
DEVICE_INLINE void fe_canon(Fe* a);
DEVICE_INLINE void fe_add(Fe* r, const Fe* a, const Fe* b);
DEVICE_INLINE void fe_sub(Fe* r, const Fe* a, const Fe* b);
DEVICE void fe_mul(Fe* r, const Fe* a, const Fe* b);
DEVICE_INLINE void fe_sqr(Fe* r, const Fe* a);
DEVICE void fe_pow(Fe* r, const Fe* base, CONSTANT const u32* exp);
DEVICE void fe_inv(Fe* r, const Fe* a);
DEVICE_INLINE void fe_from_bytes(Fe* r, const u8* in32);
DEVICE_INLINE void fe_from_bytes_g(Fe* r, GLOBAL const u8* in32);   /* new: same body, GLOBAL source */
DEVICE_INLINE void fe_to_bytes(u8* out32, const Fe* a);
DEVICE_INLINE void fe_load_const(Fe* r, CONSTANT const u32* limbs);
DEVICE_INLINE void pt_identity(Pt* r);
DEVICE_INLINE void pt_base(Pt* r);
DEVICE void pt_add(Pt* r, const Pt* p, const Pt* q);
DEVICE void pt_dbl(Pt* r, const Pt* p);
DEVICE void scalarmult_base(Pt* r, const u8* scalar32);
DEVICE void pt_compress(u8* out32, const Pt* p);
DEVICE_INLINE u64 rotr64(u64 x, int n);
DEVICE_INLINE u32 rotr32(u32 x, int n);
DEVICE_INLINE u32 rotl32(u32 x, int n);
DEVICE void sha512(const u8* msg, int len, u8* out64);
DEVICE void sha256(const u8* msg, int len, u8* out32);
DEVICE_INLINE u32 rmd_f(int j, u32 x, u32 y, u32 z);
DEVICE void ripemd160(const u8* msg, int len, u8* out20);
DEVICE int base58_encode(const u8* in, int len, char* out);
DEVICE void scalarmult_base_windowed(Pt* r, const u8* a, GLOBAL const u8* table);   /* forward declaration kept */
DEVICE void derive_address(const u8* entropy16, u8* out36, GLOBAL const u8* table);
DEVICE void prf_entropy(const u8* key32, u64 counter, u8* out16);
DEVICE_INLINE int cmp_bytes(GLOBAL const u8* a, const u8* b, int len);
DEVICE bool group_has(GLOBAL const u8* chars, u32 offset, u32 count, u32 len, const u8* slice);
DEVICE bool matches_any(const u8* body, int bodyLen, GLOBAL const u32* groups, int groupCount, GLOBAL const u8* chars);
DEVICE void precomp_from_pt(Precomp* o, const Pt* p);
DEVICE_INLINE void precomp_identity(Precomp* o);
DEVICE_INLINE void precomp_load(Precomp* o, GLOBAL const u8* table, int index);   /* uses fe_from_bytes_g */
DEVICE_INLINE void precomp_neg(Precomp* o);
DEVICE void pt_madd(Pt* r, const Pt* p, const Precomp* q);
DEVICE_INLINE void select_precomp(Precomp* o, GLOBAL const u8* table, int k, int e);
```

- [ ] **Step 1: Write the shims**

`gpu/kernels/cuda_shim.h`:

```c
// CUDA shim for the single-source kernel body. build-ptx.py concatenates this file and
// xrpl_vanity_body.h and hands the result to NVRTC. Keep in lockstep with opencl_shim.h.
typedef unsigned char u8;
typedef unsigned int u32;
typedef unsigned long long u64;
typedef signed char i8;

#define KERNEL extern "C" __global__
#define DEVICE __device__
#define DEVICE_INLINE __device__ __forceinline__
#define GLOBAL
#define CONSTANT
#define CONSTANT_TABLE __device__ __constant__
#define GLOBAL_ID() (blockIdx.x * blockDim.x + threadIdx.x)
#define U64_C(x) x##ull
#define ATOMIC_INC(p) atomicAdd((p), 1u)
```

`gpu/kernels/opencl_shim.h`:

```c
// OpenCL C 1.2 shim for the single-source kernel body. The host concatenates this file and
// xrpl_vanity_body.h and compiles them at run time with -cl-std=CL1.2. Keep in lockstep with
// cuda_shim.h. No generic address space is assumed: GLOBAL marks the only pointers into
// device buffers that device functions accept.
typedef unsigned char u8;
typedef unsigned int u32;
typedef ulong u64;
typedef char i8;

#define KERNEL __kernel
#define DEVICE
#define DEVICE_INLINE static inline
#define GLOBAL __global
#define CONSTANT __constant
#define CONSTANT_TABLE __constant
#define GLOBAL_ID() ((int)get_global_id(0))
#define U64_C(x) x##ul
#define ATOMIC_INC(p) atomic_inc((p))
```

- [ ] **Step 2: Create the body from the current kernel**

```bash
cd xrpl-vanity
git mv gpu/kernels/xrpl_vanity.cu gpu/kernels/xrpl_vanity_body.h
```

Then transform `xrpl_vanity_body.h` with these rules (apply with an editor; verify with the greps in Step 4):

| Old | New |
| --- | --- |
| the four `typedef` lines at the top | delete (the shim provides them); replace the header comment with: `// Single-source kernel body for the CUDA and OpenCL backends. Compiled after cuda_shim.h (NVRTC) or opencl_shim.h (vendor OpenCL compiler). See docs/2026-09-03-xrpl-vanity-gpu-opencl-design.md for the discipline this file follows.` |
| `struct Fe { u32 v[8]; };` / `struct Pt { Fe X, Y, Z, T; };` / `struct Precomp { Fe ypx, ymx, xy2d; };` | `typedef struct { u32 v[8]; } Fe;` / `typedef struct { Fe X, Y, Z, T; } Pt;` / `typedef struct { Fe ypx, ymx, xy2d; } Precomp;` |
| `__device__ __constant__ T NAME[N] = {` | `CONSTANT_TABLE T NAME[N] = {` |
| `__device__ __forceinline__` | `DEVICE_INLINE` |
| `__device__` (remaining) | `DEVICE` |
| `extern "C" __global__ void k_…(` | `KERNEL void k_…(` |
| `int i = blockIdx.x * blockDim.x + threadIdx.x;` | `int i = GLOBAL_ID();` |
| `Fe& r` / `const Fe& a` (and `Pt&`, `Precomp&`) parameters | `Fe* r` / `const Fe* a` (and `Pt*`, `Precomp*`) |
| inside those functions: `r.v[i]` → `r->v[i]`, `a.v[i]` → `a->v[i]`, `p.X` → `p->X`, `q.ymx` → `q->ymx`, `o.ypx` → `o->ypx` … (every member access through a parameter) | as shown |
| calls passing a local: `fe_add(A, t1, t2)` → `fe_add(&A, &t1, &t2)`; passing a struct member of a pointer param: `fe_sub(t1, p.Y, p.X)` → `fe_sub(&t1, &p->Y, &p->X)`; `fe_mul(r.X, E, F)` → `fe_mul(&r->X, &E, &F)`; `fe_copy(r, o)` → `fe_copy(r, &o)`; `pt_dbl(R, R)` → `pt_dbl(&R, &R)`; `pt_add(R, R, B)` → `pt_add(&R, &R, &B)`; `pt_madd(h, h, t)` → `pt_madd(&h, &h, &t)`; `select_precomp(t, table, …)` → `select_precomp(&t, table, …)` | as shown |
| `r = R;` / `r = h;` (struct assignment to a reference) | `*r = R;` / `*r = h;` |
| `fe_pow(Fe& r, const Fe& base, const u32* exp)` / `fe_load_const(Fe& r, const u32* limbs)` | `fe_pow(Fe* r, const Fe* base, CONSTANT const u32* exp)` / `fe_load_const(Fe* r, CONSTANT const u32* limbs)` |
| every `…ull` literal: `38ull`, `0xffffffffull`, `1ull`, the 80 `K512` entries `0x…ull` | `U64_C(38)`, `U64_C(0xffffffff)`, `U64_C(1)`, `U64_C(0x…)` |
| `(u64)len * 8ull` | `(u64)len * U64_C(8)` |
| `signed char` | `i8` |
| `atomicAdd(hitCount, 1u)` | `ATOMIC_INC(hitCount)` |
| `bool` return types / locals in device functions | keep |

Address-space rules — apply exactly these signatures (from the Produces block) and bodies:

1. Add, directly under `fe_from_bytes`, a GLOBAL-source twin (body identical except the parameter qualifier):

```c
DEVICE_INLINE void fe_from_bytes_g(Fe* r, GLOBAL const u8* in32)
{
    for (int i = 0; i < 8; i++) {
        r->v[i] = (u32)in32[4 * i] | ((u32)in32[4 * i + 1] << 8) | ((u32)in32[4 * i + 2] << 16) | ((u32)in32[4 * i + 3] << 24);
    }
    fe_canon(r);
}
```

2. `precomp_load` reads the table with `fe_from_bytes_g` (its `table` parameter is `GLOBAL const u8*`); `select_precomp`, `scalarmult_base_windowed`, `derive_address` carry `GLOBAL const u8* table`; `cmp_bytes(GLOBAL const u8* a, const u8* b, int len)`; `group_has(GLOBAL const u8* chars, …, const u8* slice)`; `matches_any(const u8* body, int bodyLen, GLOBAL const u32* groups, int groupCount, GLOBAL const u8* chars)`.

3. Every kernel copies its per-thread input into a private array and its output from a private array. Replace the thirteen kernels with exactly these:

```c
KERNEL void k_add_arrays(GLOBAL const u32* a, GLOBAL const u32* b, GLOBAL u32* out, int n)
{
    int i = GLOBAL_ID();
    if (i >= n) return;
    out[i] = a[i] + b[i];
}

KERNEL void k_fe_op(GLOBAL const u32* a, GLOBAL const u32* b, GLOBAL u32* out, int n, int op)
{
    int i = GLOBAL_ID();
    if (i >= n) return;
    Fe x, y, r;
    for (int k = 0; k < 8; k++) { x.v[k] = a[i * 8 + k]; y.v[k] = b[i * 8 + k]; }
    switch (op) {
        case 0: fe_add(&r, &x, &y); break;
        case 1: fe_sub(&r, &x, &y); break;
        case 2: fe_mul(&r, &x, &y); break;
        case 3: fe_sqr(&r, &x); break;
        case 4: fe_inv(&r, &x); break;
        case 6: {
            u8 raw[32];
            fe_to_bytes(raw, &x);
            fe_from_bytes(&r, raw);
            break;
        }
        default: fe_copy(&r, &x); fe_canon(&r); break;
    }
    for (int k = 0; k < 8; k++) out[i * 8 + k] = r.v[k];
}

KERNEL void k_scalarmult_base(GLOBAL const u8* scalars, GLOBAL u8* pubs, int n)
{
    int i = GLOBAL_ID();
    if (i >= n) return;
    u8 s[32];
    for (int j = 0; j < 32; j++) s[j] = scalars[i * 32 + j];
    Pt P;
    scalarmult_base(&P, s);
    u8 pub[32];
    pt_compress(pub, &P);
    for (int j = 0; j < 32; j++) pubs[i * 32 + j] = pub[j];
}

KERNEL void k_sha512(GLOBAL const u8* in, GLOBAL u8* out, int n, int inLen)
{
    int i = GLOBAL_ID();
    if (i >= n) return;
    if (inLen < 0 || inLen > 239) return;
    u8 msg[240];
    for (int j = 0; j < inLen; j++) msg[j] = in[i * inLen + j];
    u8 h[64];
    sha512(msg, inLen, h);
    for (int j = 0; j < 64; j++) out[i * 64 + j] = h[j];
}

KERNEL void k_sha256(GLOBAL const u8* in, GLOBAL u8* out, int n, int inLen)
{
    int i = GLOBAL_ID();
    if (i >= n) return;
    if (inLen < 0 || inLen > 183) return;
    u8 msg[184];
    for (int j = 0; j < inLen; j++) msg[j] = in[i * inLen + j];
    u8 h[32];
    sha256(msg, inLen, h);
    for (int j = 0; j < 32; j++) out[i * 32 + j] = h[j];
}

KERNEL void k_ripemd160(GLOBAL const u8* in, GLOBAL u8* out, int n, int inLen)
{
    int i = GLOBAL_ID();
    if (i >= n) return;
    if (inLen < 0 || inLen > 119) return;
    u8 msg[120];
    for (int j = 0; j < inLen; j++) msg[j] = in[i * inLen + j];
    u8 h[20];
    ripemd160(msg, inLen, h);
    for (int j = 0; j < 20; j++) out[i * 20 + j] = h[j];
}

KERNEL void k_base58(GLOBAL const u8* in, GLOBAL u8* out, int n)
{
    int i = GLOBAL_ID();
    if (i >= n) return;
    u8 payload[25];
    for (int j = 0; j < 25; j++) payload[j] = in[i * 25 + j];
    char buf[48];
    int len = base58_encode(payload, 25, buf);
    out[i * 36] = (u8)len;
    for (int j = 0; j < 35; j++) out[i * 36 + 1 + j] = (j < len) ? (u8)buf[j] : 0;
}

KERNEL void k_derive_address(GLOBAL const u8* entropy, GLOBAL u8* out, int n, GLOBAL const u8* table)
{
    int i = GLOBAL_ID();
    if (i >= n) return;
    u8 e[16];
    for (int j = 0; j < 16; j++) e[j] = entropy[i * 16 + j];
    u8 rec[36];
    derive_address(e, rec, table);
    for (int j = 0; j < 36; j++) out[i * 36 + j] = rec[j];
}

KERNEL void k_prf_entropy(GLOBAL const u8* key32, u64 start, GLOBAL u8* out, int n)
{
    int i = GLOBAL_ID();
    if (i >= n) return;
    u8 key[32];
    for (int j = 0; j < 32; j++) key[j] = key32[j];
    u8 e[16];
    prf_entropy(key, start + (u64)i, e);
    for (int j = 0; j < 16; j++) out[i * 16 + j] = e[j];
}

KERNEL void k_match_records(GLOBAL const u8* records, int n, GLOBAL const u32* groups, int groupCount, GLOBAL const u8* chars, GLOBAL u8* flags)
{
    int i = GLOBAL_ID();
    if (i >= n) return;
    u8 rec[36];
    for (int j = 0; j < 36; j++) rec[j] = records[i * 36 + j];
    int len = rec[0];
    flags[i] = (len >= 1 && matches_any(rec + 2, len - 1, groups, groupCount, chars)) ? 1 : 0;
}

KERNEL void k_search(GLOBAL const u8* key32, u64 start, int n,
                     GLOBAL const u32* groups, int groupCount, GLOBAL const u8* chars,
                     GLOBAL u8* hits, GLOBAL u32* hitCount, int hitCapacity, GLOBAL const u8* table)
{
    int i = GLOBAL_ID();
    if (i >= n) return;
    u8 key[32];
    for (int j = 0; j < 32; j++) key[j] = key32[j];
    u8 e[16];
    prf_entropy(key, start + (u64)i, e);
    u8 rec[36];
    derive_address(e, rec, table);
    int len = rec[0];
    if (len >= 1 && matches_any(rec + 2, len - 1, groups, groupCount, chars)) {
        u32 slot = ATOMIC_INC(hitCount);
        if ((int)slot < hitCapacity) {
            GLOBAL u8* h = hits + (u32)slot * 52u;
            for (int j = 0; j < 16; j++) h[j] = e[j];
            for (int j = 0; j < 36; j++) h[16 + j] = rec[j];
        }
    }
}

KERNEL void k_build_base_table(GLOBAL u8* table)
{
    int t = GLOBAL_ID();
    if (t >= 256) return;
    int k = t / 8;
    int j = t % 8;
    u8 s[32];
    for (int i = 0; i < 32; i++) s[i] = 0;
    s[k] = (u8)(j + 1);
    Pt P;
    scalarmult_base(&P, s);
    Precomp pc;
    precomp_from_pt(&pc, &P);
    u8 entry[96];
    fe_to_bytes(entry, &pc.ypx);
    fe_to_bytes(entry + 32, &pc.ymx);
    fe_to_bytes(entry + 64, &pc.xy2d);
    for (int i = 0; i < 96; i++) table[t * 96 + i] = entry[i];
}

KERNEL void k_scalarmult_base_windowed(GLOBAL const u8* scalars, GLOBAL u8* pubs, int n, GLOBAL const u8* table)
{
    int i = GLOBAL_ID();
    if (i >= n) return;
    u8 s[32];
    for (int j = 0; j < 32; j++) s[j] = scalars[i * 32 + j];
    Pt P;
    scalarmult_base_windowed(&P, s, table);
    u8 pub[32];
    pt_compress(pub, &P);
    for (int j = 0; j < 32; j++) pubs[i * 32 + j] = pub[j];
}
```

Representative transformed device functions (the rest follow the same rules):

```c
DEVICE_INLINE void fe_add(Fe* r, const Fe* a, const Fe* b)
{
    u64 c = 0;
    for (int i = 0; i < 8; i++) {
        c += (u64)a->v[i] + (u64)b->v[i];
        r->v[i] = (u32)c;
        c >>= 32;
    }
    fe_cond_sub_p(r);
}

DEVICE void pt_add(Pt* r, const Pt* p, const Pt* q)
{
    Fe A, B, C, D, E, F, G, H, t1, t2, d2;
    fe_sub(&t1, &p->Y, &p->X); fe_sub(&t2, &q->Y, &q->X); fe_mul(&A, &t1, &t2);
    fe_add(&t1, &p->Y, &p->X); fe_add(&t2, &q->Y, &q->X); fe_mul(&B, &t1, &t2);
    fe_load_const(&d2, D2_LIMBS); fe_mul(&t1, &p->T, &d2); fe_mul(&C, &t1, &q->T);
    fe_add(&t1, &p->Z, &p->Z); fe_mul(&D, &t1, &q->Z);
    fe_sub(&E, &B, &A); fe_sub(&F, &D, &C); fe_add(&G, &D, &C); fe_add(&H, &B, &A);
    fe_mul(&r->X, &E, &F); fe_mul(&r->Y, &G, &H); fe_mul(&r->Z, &F, &G); fe_mul(&r->T, &E, &H);
}

DEVICE void scalarmult_base(Pt* r, const u8* scalar32)
{
    Pt R, B;
    pt_identity(&R);
    pt_base(&B);
    for (int i = 255; i >= 0; i--) {
        pt_dbl(&R, &R);
        if ((scalar32[i >> 3] >> (i & 7)) & 1u) pt_add(&R, &R, &B);
    }
    *r = R;
}

DEVICE_INLINE void precomp_load(Precomp* o, GLOBAL const u8* table, int index)
{
    GLOBAL const u8* e = table + index * 96;
    fe_from_bytes_g(&o->ypx, e);
    fe_from_bytes_g(&o->ymx, e + 32);
    fe_from_bytes_g(&o->xy2d, e + 64);
}

DEVICE bool group_has(GLOBAL const u8* chars, u32 offset, u32 count, u32 len, const u8* slice)
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
```

- [ ] **Step 3: Make `build-ptx.py` concatenate the shim and the body**

In `gpu/build-ptx.py` replace the `CU = …` constant and the source read with:

```python
SHIM = os.path.join(ROOT, "gpu", "kernels", "cuda_shim.h")
BODY = os.path.join(ROOT, "gpu", "kernels", "xrpl_vanity_body.h")
```

and

```python
    with open(SHIM, "rb") as f:
        shim = f.read()
    with open(BODY, "rb") as f:
        body = f.read()
    source = shim + b"\n" + body
```

(keep passing `b"xrpl_vanity.cu"` as the program name to NVRTC). Update the docstring's first line to "Compile cuda_shim.h + xrpl_vanity_body.h to PTX with NVRTC."

Regenerate: `python gpu/build-ptx.py` — it must succeed with no NVRTC errors.

- [ ] **Step 4: Verify the discipline mechanically**

All of these must print nothing:

```bash
cd xrpl-vanity
grep -nE "Fe&|Pt&|Precomp&" gpu/kernels/xrpl_vanity_body.h
grep -nE "[0-9a-fA-F]ull\b|[0-9a-fA-F]ul\b" gpu/kernels/xrpl_vanity_body.h
grep -nE "__device__|__global__|__constant__|__forceinline__|extern \"C\"|blockIdx|threadIdx|atomicAdd|signed char|unsigned long|size_t" gpu/kernels/xrpl_vanity_body.h
grep -nE "^struct " gpu/kernels/xrpl_vanity_body.h
ls gpu/kernels/xrpl_vanity.cu 2>/dev/null
```

And these must show every kernel taking `GLOBAL` buffers:

```bash
grep -n "^KERNEL" gpu/kernels/xrpl_vanity_body.h | wc -l     # 13
grep -c "U64_C(" gpu/kernels/xrpl_vanity_body.h              # >= 84 (80 K512 entries + folds + lengths)
```

- [ ] **Step 5: Run the whole GPU suite on CUDA (the no-op gate)**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release`
Expected: identical totals to before the refactor (90 tests, 0 failed, 0 skipped on the reference machine), including `GpuPipelineTests.One_Million_Random_Entropies_Match_The_Cpu_Reference`, `GpuWindowedTests`, `GpuMatcherTests`, `GpuSearchTests`. Build warning-free. If anything fails, the refactor introduced a transcription error — fix the body, regenerate the PTX, re-run; do not touch the tests.

- [ ] **Step 6: Commit**

```bash
git add gpu/kernels/cuda_shim.h gpu/kernels/opencl_shim.h gpu/kernels/xrpl_vanity_body.h gpu/kernels/xrpl_vanity.ptx gpu/build-ptx.py
git add -u gpu/kernels
git commit -m "refactor(gpu): single-source kernel body with CUDA and OpenCL shims, verified as a no-op on CUDA"
```

---

### Task 2: Backend abstraction, CUDA adapter, tests migrated (still CUDA-only)

**Files:**
- Create: `src/XrplVanity.Gpu/IGpuBackend.cs`, `src/XrplVanity.Gpu/GpuBackends.cs`, `src/XrplVanity.Gpu/Cuda/CudaBackend.cs`
- Modify: `tests/XrplVanity.Tests/Gpu/GpuTestHelper.cs` and every GPU test file (`GpuInfrastructureTests`, `GpuFieldTests`, `GpuPointTests`, `GpuHashTests`, `GpuPipelineTests`, `GpuMatcherTests`, `GpuSearchTests`, `GpuWindowedTests`, `GpuCliTests`)
- Test: the migrated suite

**Interfaces:**
- Consumes: `CudaContext`, `CudaModule`, `CudaFunction`, `DeviceBuffer`, `GpuKernels.ReadPtx()`.
- Produces (namespace `XrplVanity.Gpu`):

```csharp
public sealed record GpuDeviceInfo(string Backend, int Index, string Name, string Details);
public interface IGpuKernel { }
public interface IGpuBuffer : IDisposable { long Size { get; } void CopyFrom(ReadOnlySpan<byte> source); void CopyTo(Span<byte> destination); void Clear(); }
public interface IGpuDevice : IDisposable
{
    string Backend { get; }           // "cuda" | "opencl"
    string Name { get; }
    IGpuKernel GetKernel(string name);
    IGpuBuffer Alloc(long bytes);
    /// threads = number of work-items required; the device rounds up to whole blocks; kernels keep their `i >= n` guard.
    void Launch(IGpuKernel kernel, int threads, int blockSize, params object[] args);   // args: IGpuBuffer, ulong, long, int, uint, byte
    void Synchronize();
}
public interface IGpuBackend { string Name { get; } IReadOnlyList<GpuDeviceInfo> EnumerateDevices(); IGpuDevice Open(int index); }
public static class GpuBackends
{
    public static IReadOnlyList<IGpuBackend> All { get; }                       // CudaBackend (Task 3 adds OpenClBackend after it)
    public static IGpuBackend? Get(string name);                                   // case-insensitive
    public static bool TryOpenFirst(out IGpuDevice? device, out string reason);   // first backend with a device
    public static bool TryOpen(string backend, int index, out IGpuDevice? device, out string reason);
}
```
- Test helper: `GpuTestHelper.RequireDevice(string backend) -> IGpuDevice` (skips when unavailable); `public static IEnumerable<object[]> Backends()` yielding `["cuda"]` and `["opencl"]` (both always yielded; unavailable ones skip at run time).

- [ ] **Step 1: Write the abstraction and the CUDA adapter**

`src/XrplVanity.Gpu/IGpuBackend.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace XrplVanity.Gpu;

public sealed record GpuDeviceInfo(string Backend, int Index, string Name, string Details);

public interface IGpuKernel
{
}

public interface IGpuBuffer : IDisposable
{
    long Size { get; }
    void CopyFrom(ReadOnlySpan<byte> source);
    void CopyTo(Span<byte> destination);
    void Clear();
}

/// <summary>One opened device with the kernel program loaded. Not thread-safe; one per thread.</summary>
public interface IGpuDevice : IDisposable
{
    string Backend { get; }
    string Name { get; }
    IGpuKernel GetKernel(string name);
    IGpuBuffer Alloc(long bytes);

    /// <summary>
    /// Runs <paramref name="kernel"/> for at least <paramref name="threads"/> work-items in
    /// blocks of <paramref name="blockSize"/>; the device rounds up to whole blocks, so kernels
    /// keep their <c>if (i >= n) return;</c> guard. Arguments must match the kernel parameter
    /// list exactly: <see cref="IGpuBuffer"/> for pointers; <c>ulong</c>, <c>long</c>, <c>int</c>,
    /// <c>uint</c>, <c>byte</c> for scalars.
    /// </summary>
    void Launch(IGpuKernel kernel, int threads, int blockSize, params object[] args);

    void Synchronize();
}

public interface IGpuBackend
{
    string Name { get; }
    IReadOnlyList<GpuDeviceInfo> EnumerateDevices();
    IGpuDevice Open(int index);
}
```

`src/XrplVanity.Gpu/Cuda/CudaBackend.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace XrplVanity.Gpu.Cuda;

public sealed class CudaBackend : IGpuBackend
{
    public string Name => "cuda";

    /// <summary>Device 0 only until Plan E extends <see cref="CudaContext"/> with a device ordinal.</summary>
    public IReadOnlyList<GpuDeviceInfo> EnumerateDevices()
    {
        if (!CudaContext.TryCreate(out CudaContext? context, out _))
        {
            return Array.Empty<GpuDeviceInfo>();
        }

        using (context)
        {
            return new[]
            {
                new GpuDeviceInfo(Name, 0, context!.DeviceName, $"sm_{context.ComputeCapabilityMajor}{context.ComputeCapabilityMinor}"),
            };
        }
    }

    public IGpuDevice Open(int index)
    {
        if (index != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "The CUDA backend exposes device 0 only.");
        }

        if (!CudaContext.TryCreate(out CudaContext? context, out string reason))
        {
            throw new InvalidOperationException($"CUDA is not available: {reason}");
        }

        return new CudaDevice(context!);
    }
}

internal sealed class CudaKernel : IGpuKernel
{
    internal CudaKernel(CudaFunction function)
    {
        Function = function;
    }

    internal CudaFunction Function { get; }
}

internal sealed class CudaBufferAdapter : IGpuBuffer
{
    internal CudaBufferAdapter(DeviceBuffer buffer)
    {
        Buffer = buffer;
    }

    internal DeviceBuffer Buffer { get; }
    public long Size => Buffer.Size;
    public void CopyFrom(ReadOnlySpan<byte> source) => Buffer.CopyFrom(source);
    public void CopyTo(Span<byte> destination) => Buffer.CopyTo(destination);
    public void Clear() => Buffer.Clear();
    public void Dispose() => Buffer.Dispose();
}

internal sealed class CudaDevice : IGpuDevice
{
    private readonly CudaContext _context;
    private readonly CudaModule _module;

    internal CudaDevice(CudaContext context)
    {
        _context = context;
        _module = context.LoadModule(GpuKernels.ReadPtx());
    }

    public string Backend => "cuda";
    public string Name => $"{_context.DeviceName} (sm_{_context.ComputeCapabilityMajor}{_context.ComputeCapabilityMinor})";

    public IGpuKernel GetKernel(string name) => new CudaKernel(_module.GetFunction(name));

    public IGpuBuffer Alloc(long bytes) => new CudaBufferAdapter(_context.Alloc(bytes));

    public void Launch(IGpuKernel kernel, int threads, int blockSize, params object[] args)
    {
        object[] unwrapped = new object[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            unwrapped[i] = args[i] is CudaBufferAdapter adapter ? adapter.Buffer : args[i];
        }

        int grid = (threads + blockSize - 1) / blockSize;
        _context.Launch(((CudaKernel)kernel).Function, grid, blockSize, unwrapped);
    }

    public void Synchronize() => _context.Synchronize();

    public void Dispose() => _context.Dispose();
}
```

`src/XrplVanity.Gpu/GpuBackends.cs`:

```csharp
using System;
using System.Collections.Generic;
using XrplVanity.Gpu.Cuda;

namespace XrplVanity.Gpu;

public static class GpuBackends
{
    /// <summary>Preference order: CUDA first (verified, fastest on NVIDIA), then OpenCL (Task 3).</summary>
    public static IReadOnlyList<IGpuBackend> All { get; } = new IGpuBackend[] { new CudaBackend() };

    public static IGpuBackend? Get(string name)
    {
        foreach (IGpuBackend backend in All)
        {
            if (string.Equals(backend.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return backend;
            }
        }

        return null;
    }

    public static bool TryOpenFirst(out IGpuDevice? device, out string reason)
    {
        List<string> reasons = new();
        foreach (IGpuBackend backend in All)
        {
            if (backend.EnumerateDevices().Count > 0)
            {
                try
                {
                    device = backend.Open(0);
                    reason = string.Empty;
                    return true;
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
                {
                    reasons.Add($"{backend.Name}: {ex.Message}");
                }
            }
            else
            {
                reasons.Add($"{backend.Name}: no devices");
            }
        }

        device = null;
        reason = string.Join("; ", reasons);
        return false;
    }

    public static bool TryOpen(string backendName, int index, out IGpuDevice? device, out string reason)
    {
        IGpuBackend? backend = Get(backendName);
        if (backend is null)
        {
            device = null;
            reason = $"unknown backend '{backendName}' (known: cuda, opencl)";
            return false;
        }

        try
        {
            device = backend.Open(index);
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            device = null;
            reason = ex.Message;
            return false;
        }
    }
}
```

- [ ] **Step 2: Migrate the test helper and every GPU test**

`GpuTestHelper` — replace `RequireContext()` with:

```csharp
    public static IEnumerable<object[]> Backends()
    {
        yield return new object[] { "cuda" };
        yield return new object[] { "opencl" };
    }

    /// <summary>Opens device 0 of the backend, or skips the calling test when it is unavailable / pre-Turing.</summary>
    public static IGpuDevice RequireDevice(string backend)
    {
        if (!GpuBackends.TryOpen(backend, 0, out IGpuDevice? device, out string reason))
        {
            Assert.Skip($"{backend} not available: {reason}");
        }

        if (backend == "cuda")
        {
            // Keep the Plan A compute-capability floor for the PTX (compute_75).
            if (CudaContext.TryCreate(out CudaContext? probe, out _))
            {
                using (probe)
                {
                    int capability = probe!.ComputeCapabilityMajor * 10 + probe.ComputeCapabilityMinor;
                    if (capability < 75)
                    {
                        device!.Dispose();
                        Assert.Skip($"CUDA device {probe.DeviceName} is compute capability {probe.ComputeCapabilityMajor}.{probe.ComputeCapabilityMinor}; the PTX targets compute_75 (Turing) or newer");
                    }
                }
            }
        }

        return device!;
    }
```

Migration rules for every test method that used the CUDA layer directly:

| Old | New |
| --- | --- |
| `[Fact] public void X()` | `[Theory] [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))] public void X(string backend)` — `[InlineData]` theories (RFC vectors, hash lengths) become `[Theory]` with an explicit `MemberData` producing the cross-product: replace `[InlineData(a, b)]` rows with a `public static IEnumerable<object[]> Rows()` that yields `[backend, a, b]` for each backend in `GpuTestHelper.Backends()` and each original row; method signature gains `string backend` as the FIRST parameter |
| `using CudaContext context = GpuTestHelper.RequireContext();` | `using IGpuDevice device = GpuTestHelper.RequireDevice(backend);` |
| `CudaModule module = context.LoadModule(GpuKernels.ReadPtx()); CudaFunction fn = module.GetFunction("k_x");` | `IGpuKernel fn = device.GetKernel("k_x");` |
| `using DeviceBuffer b = context.Alloc(n);` | `using IGpuBuffer b = device.Alloc(n);` |
| `context.Launch(fn, (n + 255) / 256, 256, …)` | `device.Launch(fn, n, 256, …)` (threads, not blocks) |
| `context.Launch(fn, (n + 127) / 128, 128, …)` | `device.Launch(fn, n, 128, …)` |
| `context.Launch(module.GetFunction("k_build_base_table"), 2, 128, table)` | `device.Launch(device.GetKernel("k_build_base_table"), 256, 128, table)` |
| `context.Synchronize()` | `device.Synchronize()` |
| `GpuPipelineTests.RunDerive`'s batching via `din.Pointer + offset` | per-batch buffers: for each batch allocate `IGpuBuffer bin = device.Alloc(batchLength * 16)` and `bout = device.Alloc(batchLength * 36)`, `bin.CopyFrom(entropies.AsSpan(batchStart * 16, batchLength * 16))`, launch with `batchLength` threads and `table`, `Synchronize()`, `bout.CopyTo(output.AsSpan(batchStart * 36, batchLength * 36))`, dispose both |
| `GpuInfrastructureTests.Context_Exposes_Device_Name_And_Compute_Capability` | keep CUDA-specific: leave as a `[Fact]` that calls `CudaContext.TryCreate` directly and skips when unavailable (it tests the CUDA layer, not the abstraction) |
| `GpuInfrastructureTests.Device_Buffer_Roundtrips_Bytes` / `Embedded_Ptx_Loads_And_A_Kernel_Runs` | migrate to the abstraction (theory over backends); rename the latter `Kernel_Program_Loads_And_A_Kernel_Runs` |
| `GpuSearchTests` — `new GpuSearchEngine(context, patterns)` | leave the engine on `CudaContext` in this task; migrate `GpuSearchTests` in Task 5 together with the engine (they stay `[Fact]` + `RequireContext` until then — so keep a `RequireContext()` helper in `GpuTestHelper` for Task 5 to remove: `public static CudaContext RequireContext() { if (!CudaContext.TryCreate(out var c, out var r)) Assert.Skip($"CUDA not available: {r}"); return c!; }`) |
| `GpuCliTests` probe `using CudaContext probe = GpuTestHelper.RequireContext(); probe.Dispose();` | unchanged in this task (Task 5) |

- [ ] **Step 3: Run the suite**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release`
Expected: every migrated theory has two rows; the `opencl` rows report Skipped ("opencl not available: unknown backend 'opencl'"), the `cuda` rows pass; totals: all `cuda` rows green, `opencl` rows skipped, no failures; warning-free.

- [ ] **Step 4: Commit**

```bash
git add src/XrplVanity.Gpu tests/XrplVanity.Tests
git commit -m "feat(gpu): backend abstraction with a CUDA adapter; GPU tests parameterised over backends"
```

---

### Task 3: OpenCL backend — P/Invoke layer, device, runtime compilation, first launch

**Files:**
- Create: `src/XrplVanity.Gpu/OpenCl/OpenClNative.cs`, `src/XrplVanity.Gpu/OpenCl/OpenClException.cs`, `src/XrplVanity.Gpu/OpenCl/OpenClBackend.cs`
- Modify: `src/XrplVanity.Gpu/GpuKernels.cs` (+ `ReadOpenClSource()`), `src/XrplVanity.Gpu/XrplVanity.Gpu.csproj` (embed the two source files), `src/XrplVanity.Gpu/GpuBackends.cs` (register `OpenClBackend` after CUDA)
- Test: `tests/XrplVanity.Tests/Gpu/GpuInfrastructureTests.cs` (the theories now run their `opencl` rows)

**Interfaces:**
- Consumes: `IGpuBackend`/`IGpuDevice`/`IGpuBuffer`/`IGpuKernel`, the embedded `opencl_shim.h` + `xrpl_vanity_body.h`.
- Produces: `OpenClBackend` (`Name = "opencl"`, enumerates all platforms × `CL_DEVICE_TYPE_ALL` devices, flat index), `OpenClDevice` (compiles on open with `-cl-std=CL1.2`), `OpenClException(int code, string operation, string message)` with `Code`, `GpuKernels.ReadOpenClSource() -> string`.

- [ ] **Step 1: Embed the OpenCL source and add the reader**

Add to `XrplVanity.Gpu.csproj` (alongside the PTX resource):

```xml
    <EmbeddedResource Include="..\..\gpu\kernels\opencl_shim.h" LogicalName="opencl_shim.h" />
    <EmbeddedResource Include="..\..\gpu\kernels\xrpl_vanity_body.h" LogicalName="xrpl_vanity_body.h" />
```

Add to `GpuKernels`:

```csharp
    /// <summary>The OpenCL C 1.2 program: opencl_shim.h followed by xrpl_vanity_body.h.</summary>
    public static string ReadOpenClSource() => ReadResource("opencl_shim.h") + "\n" + ReadResource("xrpl_vanity_body.h");

    private static string ReadResource(string name)
    {
        Assembly assembly = typeof(GpuKernels).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(name)
            ?? throw new FileNotFoundException($"Embedded resource '{name}' is missing.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
```

(and make `ReadPtx()` use `ReadResource("xrpl_vanity.ptx")`).

- [ ] **Step 2: Write the P/Invoke layer**

`src/XrplVanity.Gpu/OpenCl/OpenClException.cs`:

```csharp
using System;

namespace XrplVanity.Gpu.OpenCl;

public sealed class OpenClException : Exception
{
    public OpenClException(int code, string operation, string message)
        : base($"{operation} failed with OpenCL error {code} ({OpenClNative.ErrorName(code)}): {message}")
    {
        Code = code;
    }

    public int Code { get; }
}
```

`src/XrplVanity.Gpu/OpenCl/OpenClNative.cs`:

```csharp
using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace XrplVanity.Gpu.OpenCl;

/// <summary>OpenCL 1.2 surface over the ICD loader (OpenCL.dll on Windows, libOpenCL.so.1 on Linux).</summary>
internal static class OpenClNative
{
    private const string Lib = "opencl";

    internal const int Success = 0;
    internal const ulong DeviceTypeAll = 0xFFFFFFFF;
    internal const ulong MemReadWrite = 1;
    internal const uint PlatformVersion = 0x0901;
    internal const uint PlatformName = 0x0902;
    internal const uint DeviceName = 0x102B;
    internal const uint DeviceVersion = 0x102F;
    internal const uint ProgramBuildLog = 0x1183;

    static OpenClNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(OpenClNative).Assembly, Resolve);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != Lib)
        {
            return IntPtr.Zero;
        }

        string native = OperatingSystem.IsWindows() ? "OpenCL.dll" : "libOpenCL.so.1";
        return NativeLibrary.TryLoad(native, out IntPtr handle) ? handle : IntPtr.Zero;
    }

    [DllImport(Lib)] internal static extern int clGetPlatformIDs(uint numEntries, IntPtr[]? platforms, out uint numPlatforms);
    [DllImport(Lib)] internal static extern int clGetPlatformInfo(IntPtr platform, uint param, nuint size, byte[]? value, out nuint sizeRet);
    [DllImport(Lib)] internal static extern int clGetDeviceIDs(IntPtr platform, ulong deviceType, uint numEntries, IntPtr[]? devices, out uint numDevices);
    [DllImport(Lib)] internal static extern int clGetDeviceInfo(IntPtr device, uint param, nuint size, byte[]? value, out nuint sizeRet);
    [DllImport(Lib)] internal static extern IntPtr clCreateContext(IntPtr properties, uint numDevices, IntPtr[] devices, IntPtr callback, IntPtr userData, out int error);
    [DllImport(Lib)] internal static extern IntPtr clCreateCommandQueue(IntPtr context, IntPtr device, ulong properties, out int error);
    [DllImport(Lib)] internal static extern IntPtr clCreateProgramWithSource(IntPtr context, uint count, IntPtr[] strings, nuint[] lengths, out int error);
    [DllImport(Lib)] internal static extern int clBuildProgram(IntPtr program, uint numDevices, IntPtr[] devices, [MarshalAs(UnmanagedType.LPStr)] string options, IntPtr callback, IntPtr userData);
    [DllImport(Lib)] internal static extern int clGetProgramBuildInfo(IntPtr program, IntPtr device, uint param, nuint size, byte[]? value, out nuint sizeRet);
    [DllImport(Lib)] internal static extern IntPtr clCreateKernel(IntPtr program, [MarshalAs(UnmanagedType.LPStr)] string name, out int error);
    [DllImport(Lib)] internal static extern IntPtr clCreateBuffer(IntPtr context, ulong flags, nuint size, IntPtr hostPtr, out int error);
    [DllImport(Lib)] internal static extern int clEnqueueWriteBuffer(IntPtr queue, IntPtr buffer, uint blocking, nuint offset, nuint size, IntPtr ptr, uint numEvents, IntPtr events, IntPtr evt);
    [DllImport(Lib)] internal static extern int clEnqueueReadBuffer(IntPtr queue, IntPtr buffer, uint blocking, nuint offset, nuint size, IntPtr ptr, uint numEvents, IntPtr events, IntPtr evt);
    [DllImport(Lib)] internal static extern int clEnqueueFillBuffer(IntPtr queue, IntPtr buffer, IntPtr pattern, nuint patternSize, nuint offset, nuint size, uint numEvents, IntPtr events, IntPtr evt);
    [DllImport(Lib)] internal static extern int clSetKernelArg(IntPtr kernel, uint index, nuint size, IntPtr value);
    [DllImport(Lib)] internal static extern int clEnqueueNDRangeKernel(IntPtr queue, IntPtr kernel, uint dimensions, IntPtr globalOffset, nuint[] globalSize, nuint[] localSize, uint numEvents, IntPtr events, IntPtr evt);
    [DllImport(Lib)] internal static extern int clFinish(IntPtr queue);
    [DllImport(Lib)] internal static extern int clReleaseMemObject(IntPtr mem);
    [DllImport(Lib)] internal static extern int clReleaseKernel(IntPtr kernel);
    [DllImport(Lib)] internal static extern int clReleaseProgram(IntPtr program);
    [DllImport(Lib)] internal static extern int clReleaseCommandQueue(IntPtr queue);
    [DllImport(Lib)] internal static extern int clReleaseContext(IntPtr context);

    internal static void Check(int code, string operation, string detail = "")
    {
        if (code != Success)
        {
            throw new OpenClException(code, operation, detail.Length == 0 ? "see error name" : detail);
        }
    }

    internal static string ErrorName(int code) => code switch
    {
        0 => "CL_SUCCESS",
        -1 => "CL_DEVICE_NOT_FOUND",
        -2 => "CL_DEVICE_NOT_AVAILABLE",
        -3 => "CL_COMPILER_NOT_AVAILABLE",
        -4 => "CL_MEM_OBJECT_ALLOCATION_FAILURE",
        -5 => "CL_OUT_OF_RESOURCES",
        -6 => "CL_OUT_OF_HOST_MEMORY",
        -11 => "CL_BUILD_PROGRAM_FAILURE",
        -30 => "CL_INVALID_VALUE",
        -32 => "CL_INVALID_PLATFORM",
        -33 => "CL_INVALID_DEVICE",
        -34 => "CL_INVALID_CONTEXT",
        -36 => "CL_INVALID_COMMAND_QUEUE",
        -38 => "CL_INVALID_MEM_OBJECT",
        -44 => "CL_INVALID_PROGRAM",
        -45 => "CL_INVALID_PROGRAM_EXECUTABLE",
        -46 => "CL_INVALID_KERNEL_NAME",
        -48 => "CL_INVALID_KERNEL",
        -49 => "CL_INVALID_ARG_INDEX",
        -51 => "CL_INVALID_ARG_SIZE",
        -52 => "CL_INVALID_KERNEL_ARGS",
        -54 => "CL_INVALID_WORK_GROUP_SIZE",
        -61 => "CL_INVALID_BUFFER_SIZE",
        _ => "CL_UNKNOWN",
    };

    internal static string PlatformString(IntPtr platform, uint param)
    {
        Check(clGetPlatformInfo(platform, param, 0, null, out nuint size), "clGetPlatformInfo(size)");
        byte[] buffer = new byte[(int)size];
        Check(clGetPlatformInfo(platform, param, size, buffer, out _), "clGetPlatformInfo");
        return System.Text.Encoding.ASCII.GetString(buffer).TrimEnd('\0', ' ');
    }

    internal static string DeviceString(IntPtr device, uint param)
    {
        Check(clGetDeviceInfo(device, param, 0, null, out nuint size), "clGetDeviceInfo(size)");
        byte[] buffer = new byte[(int)size];
        Check(clGetDeviceInfo(device, param, size, buffer, out _), "clGetDeviceInfo");
        return System.Text.Encoding.ASCII.GetString(buffer).TrimEnd('\0', ' ');
    }
}
```

- [ ] **Step 3: Write the backend and device**

`src/XrplVanity.Gpu/OpenCl/OpenClBackend.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace XrplVanity.Gpu.OpenCl;

public sealed class OpenClBackend : IGpuBackend
{
    public string Name => "opencl";

    private static bool TryEnumerate(out List<(IntPtr Platform, IntPtr Device, GpuDeviceInfo Info)> devices, out string reason)
    {
        devices = new List<(IntPtr, IntPtr, GpuDeviceInfo)>();
        try
        {
            int rc = OpenClNative.clGetPlatformIDs(0, null, out uint platformCount);
            if (rc != OpenClNative.Success || platformCount == 0)
            {
                reason = rc == OpenClNative.Success ? "no OpenCL platforms" : $"clGetPlatformIDs: {OpenClNative.ErrorName(rc)}";
                return false;
            }

            IntPtr[] platforms = new IntPtr[platformCount];
            OpenClNative.Check(OpenClNative.clGetPlatformIDs(platformCount, platforms, out _), "clGetPlatformIDs");
            int index = 0;
            foreach (IntPtr platform in platforms)
            {
                string platformName = OpenClNative.PlatformString(platform, OpenClNative.PlatformName);
                int drc = OpenClNative.clGetDeviceIDs(platform, OpenClNative.DeviceTypeAll, 0, null, out uint deviceCount);
                if (drc != OpenClNative.Success || deviceCount == 0)
                {
                    continue; // a platform with no devices is not an error
                }

                IntPtr[] ids = new IntPtr[deviceCount];
                OpenClNative.Check(OpenClNative.clGetDeviceIDs(platform, OpenClNative.DeviceTypeAll, deviceCount, ids, out _), "clGetDeviceIDs");
                foreach (IntPtr device in ids)
                {
                    string name = OpenClNative.DeviceString(device, OpenClNative.DeviceName);
                    string version = OpenClNative.DeviceString(device, OpenClNative.DeviceVersion);
                    devices.Add((platform, device, new GpuDeviceInfo(Name, index, name, $"{platformName}, {version}")));
                    index++;
                }
            }

            reason = devices.Count == 0 ? "no OpenCL devices" : string.Empty;
            return devices.Count > 0;
        }
        catch (DllNotFoundException)
        {
            reason = "OpenCL ICD loader not found (OpenCL.dll / libOpenCL.so.1)";
            return false;
        }
        catch (OpenClException ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    public IReadOnlyList<GpuDeviceInfo> EnumerateDevices()
    {
        if (!TryEnumerate(out List<(IntPtr, IntPtr, GpuDeviceInfo Info)> devices, out _))
        {
            return Array.Empty<GpuDeviceInfo>();
        }

        List<GpuDeviceInfo> infos = new(devices.Count);
        foreach ((_, _, GpuDeviceInfo info) in devices)
        {
            infos.Add(info);
        }

        return infos;
    }

    public IGpuDevice Open(int index)
    {
        if (!TryEnumerate(out List<(IntPtr Platform, IntPtr Device, GpuDeviceInfo Info)> devices, out string reason))
        {
            throw new InvalidOperationException($"OpenCL is not available: {reason}");
        }

        if (index < 0 || index >= devices.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), $"OpenCL device index {index} is out of range (0..{devices.Count - 1}).");
        }

        (IntPtr _, IntPtr device, GpuDeviceInfo info) = devices[index];
        return new OpenClDevice(device, info);
    }
}

internal sealed class OpenClKernel : IGpuKernel
{
    internal OpenClKernel(IntPtr handle)
    {
        Handle = handle;
    }

    internal IntPtr Handle { get; }
}

internal sealed class OpenClBuffer : IGpuBuffer
{
    private readonly IntPtr _queue;
    private bool _disposed;

    internal OpenClBuffer(IntPtr handle, IntPtr queue, long size)
    {
        Handle = handle;
        _queue = queue;
        Size = size;
    }

    internal IntPtr Handle { get; }
    public long Size { get; }

    public unsafe void CopyFrom(ReadOnlySpan<byte> source)
    {
        EnsureFits(source.Length);
        fixed (byte* p = source)
        {
            OpenClNative.Check(OpenClNative.clEnqueueWriteBuffer(_queue, Handle, 1, 0, (nuint)source.Length, (IntPtr)p, 0, IntPtr.Zero, IntPtr.Zero), "clEnqueueWriteBuffer");
        }
    }

    public unsafe void CopyTo(Span<byte> destination)
    {
        EnsureFits(destination.Length);
        fixed (byte* p = destination)
        {
            OpenClNative.Check(OpenClNative.clEnqueueReadBuffer(_queue, Handle, 1, 0, (nuint)destination.Length, (IntPtr)p, 0, IntPtr.Zero, IntPtr.Zero), "clEnqueueReadBuffer");
        }
    }

    public unsafe void Clear()
    {
        byte zero = 0;
        OpenClNative.Check(OpenClNative.clEnqueueFillBuffer(_queue, Handle, (IntPtr)(&zero), 1, 0, (nuint)Size, 0, IntPtr.Zero, IntPtr.Zero), "clEnqueueFillBuffer");
        OpenClNative.Check(OpenClNative.clFinish(_queue), "clFinish");
    }

    private void EnsureFits(int length)
    {
        if (length > Size)
        {
            throw new ArgumentOutOfRangeException(nameof(length), $"{length} bytes exceed the {Size}-byte device buffer.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = OpenClNative.clReleaseMemObject(Handle);
    }
}

internal sealed class OpenClDevice : IGpuDevice
{
    private readonly IntPtr _device;
    private readonly IntPtr _context;
    private readonly IntPtr _queue;
    private readonly IntPtr _program;
    private readonly List<IntPtr> _kernels = new();
    private bool _disposed;

    internal OpenClDevice(IntPtr device, GpuDeviceInfo info)
    {
        _device = device;
        Name = $"{info.Name} ({info.Details})";
        IntPtr[] devices = { device };
        _context = OpenClNative.clCreateContext(IntPtr.Zero, 1, devices, IntPtr.Zero, IntPtr.Zero, out int error);
        OpenClNative.Check(error, "clCreateContext");
        _queue = OpenClNative.clCreateCommandQueue(_context, device, 0, out error);
        OpenClNative.Check(error, "clCreateCommandQueue");
        _program = Build(GpuKernels.ReadOpenClSource());
    }

    public string Backend => "opencl";
    public string Name { get; }

    private IntPtr Build(string source)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(source);
        GCHandle pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            IntPtr[] strings = { pin.AddrOfPinnedObject() };
            nuint[] lengths = { (nuint)bytes.Length };
            IntPtr program = OpenClNative.clCreateProgramWithSource(_context, 1, strings, lengths, out int error);
            OpenClNative.Check(error, "clCreateProgramWithSource");
            IntPtr[] devices = { _device };
            int rc = OpenClNative.clBuildProgram(program, 1, devices, "-cl-std=CL1.2", IntPtr.Zero, IntPtr.Zero);
            if (rc != OpenClNative.Success)
            {
                string log = BuildLog(program);
                _ = OpenClNative.clReleaseProgram(program);
                throw new OpenClException(rc, "clBuildProgram", "OpenCL program build failed:\n" + log);
            }

            return program;
        }
        finally
        {
            pin.Free();
        }
    }

    private string BuildLog(IntPtr program)
    {
        if (OpenClNative.clGetProgramBuildInfo(program, _device, OpenClNative.ProgramBuildLog, 0, null, out nuint size) != OpenClNative.Success || size == 0)
        {
            return "<no build log>";
        }

        byte[] buffer = new byte[(int)size];
        _ = OpenClNative.clGetProgramBuildInfo(program, _device, OpenClNative.ProgramBuildLog, size, buffer, out _);
        return Encoding.ASCII.GetString(buffer).TrimEnd('\0');
    }

    public IGpuKernel GetKernel(string name)
    {
        IntPtr kernel = OpenClNative.clCreateKernel(_program, name, out int error);
        OpenClNative.Check(error, $"clCreateKernel({name})");
        _kernels.Add(kernel);
        return new OpenClKernel(kernel);
    }

    public IGpuBuffer Alloc(long bytes)
    {
        IntPtr handle = OpenClNative.clCreateBuffer(_context, OpenClNative.MemReadWrite, (nuint)bytes, IntPtr.Zero, out int error);
        OpenClNative.Check(error, "clCreateBuffer");
        return new OpenClBuffer(handle, _queue, bytes);
    }

    public void Launch(IGpuKernel kernel, int threads, int blockSize, params object[] args)
    {
        IntPtr handle = ((OpenClKernel)kernel).Handle;
        for (int i = 0; i < args.Length; i++)
        {
            SetArg(handle, (uint)i, args[i]);
        }

        nuint local = (nuint)blockSize;
        nuint global = (nuint)(((threads + blockSize - 1) / blockSize) * blockSize);
        OpenClNative.Check(
            OpenClNative.clEnqueueNDRangeKernel(_queue, handle, 1, IntPtr.Zero, new[] { global }, new[] { local }, 0, IntPtr.Zero, IntPtr.Zero),
            "clEnqueueNDRangeKernel");
    }

    private static unsafe void SetArg(IntPtr kernel, uint index, object value)
    {
        switch (value)
        {
            case OpenClBuffer buffer:
            {
                IntPtr mem = buffer.Handle;
                OpenClNative.Check(OpenClNative.clSetKernelArg(kernel, index, (nuint)IntPtr.Size, (IntPtr)(&mem)), $"clSetKernelArg({index})");
                break;
            }
            case ulong u64: OpenClNative.Check(OpenClNative.clSetKernelArg(kernel, index, 8, (IntPtr)(&u64)), $"clSetKernelArg({index})"); break;
            case long i64: OpenClNative.Check(OpenClNative.clSetKernelArg(kernel, index, 8, (IntPtr)(&i64)), $"clSetKernelArg({index})"); break;
            case int i32: OpenClNative.Check(OpenClNative.clSetKernelArg(kernel, index, 4, (IntPtr)(&i32)), $"clSetKernelArg({index})"); break;
            case uint u32: OpenClNative.Check(OpenClNative.clSetKernelArg(kernel, index, 4, (IntPtr)(&u32)), $"clSetKernelArg({index})"); break;
            case byte b: OpenClNative.Check(OpenClNative.clSetKernelArg(kernel, index, 1, (IntPtr)(&b)), $"clSetKernelArg({index})"); break;
            default: throw new ArgumentException($"Unsupported kernel argument type {value?.GetType().Name ?? "null"} at index {index}.");
        }
    }

    public void Synchronize()
    {
        OpenClNative.Check(OpenClNative.clFinish(_queue), "clFinish");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (IntPtr kernel in _kernels)
        {
            _ = OpenClNative.clReleaseKernel(kernel);
        }

        _ = OpenClNative.clReleaseProgram(_program);
        _ = OpenClNative.clReleaseCommandQueue(_queue);
        _ = OpenClNative.clReleaseContext(_context);
    }
}
```

Register it: in `GpuBackends.cs` add `using XrplVanity.Gpu.OpenCl;` and make `All = new IGpuBackend[] { new CudaBackend(), new OpenClBackend() }`.

- [ ] **Step 4: Run the infrastructure tests on both backends**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuInfrastructureTests"`
Expected: the `opencl` rows of `Device_Buffer_Roundtrips_Bytes` and `Kernel_Program_Loads_And_A_Kernel_Runs` now PASS (the OpenCL program compiles from the embedded source and `k_add_arrays` runs); the `cuda` rows still pass. If `clBuildProgram` fails, the exception message contains the vendor build log — fix the body/shim (keeping the CUDA suite green and regenerating the PTX) and re-run. Note the compile time printed by the test run; it should be seconds, not minutes.

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.Gpu tests/XrplVanity.Tests gpu/kernels
git commit -m "feat(gpu): OpenCL backend with runtime compilation of the single-source kernel"
```

---

### Task 4: Every anchor on OpenCL

**Files:**
- Modify: as required by failures — `gpu/kernels/xrpl_vanity_body.h`, `gpu/kernels/opencl_shim.h` (and regenerate the PTX whenever the body changes)
- Test: the whole GPU suite; `opencl` rows now live

**Interfaces:** none new. The deliverable is the green suite on both backends.

- [ ] **Step 1: Run each anchor class on both backends, in this order**

```bash
dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuFieldTests"
dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuHashTests"
dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuPointTests"
dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuWindowedTests"
dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuMatcherTests"
dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuPipelineTests"
```

Expected: every `opencl` row passes as well as every `cuda` row. The 1,000,000-input cross-check on OpenCL uses the per-batch buffers from Task 2 and may take longer than on CUDA; that is fine.

- [ ] **Step 2: Diagnose and fix any OpenCL failure at the source**

For a failing `opencl` row: (a) a build failure — read the vendor log in the exception; (b) a wrong result — bisect with the stage kernels (`k_fe_op` op codes, `k_sha512`/`k_sha256`/`k_ripemd160`, `k_scalarmult_base`, `k_base58`) on the failing input, exactly as the Plan A executor notes describe. Every fix goes into `xrpl_vanity_body.h` or `opencl_shim.h`; never into a test. After every body change: `python gpu/build-ptx.py`, then the full CUDA rows must still pass. Typical suspects: an `i8`/`u8` promotion difference, a literal that escaped `U64_C`, a `bool` in an unexpected place, a private-vs-`GLOBAL` pointer mismatch (the compiler reports the latter).

- [ ] **Step 3: Whole suite, both backends**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release`
Expected: all rows green on both backends (the `GpuSearchTests`/`GpuCliTests` are still CUDA-only until Task 5), 0 failures, warning-free.

- [ ] **Step 4: Commit**

```bash
git add gpu/kernels tests/XrplVanity.Tests
git commit -m "test(gpu): every correctness anchor green on the OpenCL backend"
```

(If no source change was needed, commit a note in the message body saying the body compiled and passed on OpenCL unchanged.)

---

### Task 5: Engine and CLI on the abstraction — `--backend`, `--device`, `--list-devices`

**Files:**
- Modify: `src/XrplVanity.Gpu/GpuSearchEngine.cs` (ctor takes `IGpuDevice`), `src/XrplVanity.Gpu/GpuAvailability.cs` (delegates to `GpuBackends`), `src/XrplVanity.Cli/CliOptions.cs`, `src/XrplVanity.Cli/Program.cs`, `README.md`
- Modify tests: `tests/XrplVanity.Tests/Gpu/GpuSearchTests.cs` (theory over backends; engine on `IGpuDevice`), `tests/XrplVanity.Tests/Gpu/GpuCliTests.cs`, `tests/XrplVanity.Tests/CliOptionsTests.cs`; remove `GpuTestHelper.RequireContext()`

**Interfaces:**
- Produces: `GpuSearchEngine(IGpuDevice device, PatternSet patterns)`; `GpuSearchEngine.DeviceName` = `$"{device.Backend}:{device.Name}"`; `CliOptions.Backend` (`string?`), `CliOptions.Device` (`int?`), `CliOptions.ListDevices` (`bool`); `SearchRunner.SearchGpu(CliOptions options, int launches)` resolves the device through `GpuBackends`; `SearchRunner.ResolveDevice(CliOptions options, out IGpuDevice? device, out string reason) -> bool`.

- [ ] **Step 1: Write the failing tests**

Append to `CliOptionsTests`:

```csharp
    [Fact]
    public void Parses_Backend_Device_And_List_Devices()
    {
        CliOptions options = CliOptions.Parse(new[] { "--gpu", "--backend", "opencl", "--device", "1", "abc" });
        Assert.Equal("opencl", options.Backend);
        Assert.Equal(1, options.Device);
        Assert.False(options.ListDevices);

        Assert.True(CliOptions.Parse(new[] { "--list-devices" }).ListDevices);
        Assert.Null(CliOptions.Parse(new[] { "abc" }).Backend);
        Assert.Null(CliOptions.Parse(new[] { "abc" }).Device);
    }
```

Migrate `GpuSearchTests` to `[Theory]` over `GpuTestHelper.Backends` with `using IGpuDevice device = GpuTestHelper.RequireDevice(backend); using GpuSearchEngine engine = new(device, patterns);` (all five tests, including the benchmark, whose output line names `engine.DeviceName`). Migrate `GpuCliTests`:

```csharp
    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void SearchGpu_Returns_Matches_That_Satisfy_The_Same_Pattern_Set(string backend)
    {
        using IGpuDevice probe = GpuTestHelper.RequireDevice(backend); // skips cleanly when unavailable
        probe.Dispose();

        CliOptions options = CliOptions.Parse(new[] { "--gpu", "--backend", backend, "--starts", "ab" });
        PatternSet patterns = SearchRunner.BuildPatternSet(options);

        List<Match> matches = SearchRunner.SearchGpu(options, launches: 1);

        Assert.True(matches.Count > 0);
        Assert.All(matches, m => Assert.True(patterns.TryMatch(m.Address, out _, out _)));
    }
```

- [ ] **Step 2: Run to see RED**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*CliOptionsTests"`
Expected: build error — `Backend`/`Device`/`ListDevices` missing; `GpuSearchEngine(IGpuDevice, …)` missing.

- [ ] **Step 3: Implement**

`GpuSearchEngine`: replace `CudaContext _context` with `IGpuDevice _device`; `CudaFunction _search` → `IGpuKernel _search = device.GetKernel("k_search")`; buffers `DeviceBuffer` → `IGpuBuffer`; table build `_device.Launch(_device.GetKernel("k_build_base_table"), 256, 128, _table); _device.Synchronize();`; the sub-launch `_device.Launch(_search, count, BlockSize, …)` (threads, not grid); `DeviceName => $"{_device.Backend}:{_device.Name}"`. Everything else (key, hit drain, gate, counters, dispose) unchanged. Remove the `using XrplVanity.Gpu.Cuda`-free dependency on `CudaContext` entirely.

`GpuAvailability.Detect()` → `GpuBackends.TryOpenFirst(out IGpuDevice? d, out string reason)`; on success returns `(true, $"{d.Backend}:{d.Name}")` and disposes.

`CliOptions`: properties `public string? Backend { get; init; }`, `public int? Device { get; init; }`, `public bool ListDevices { get; init; }`; cases `--backend` (value; must be `cuda` or `opencl`, else `ArgumentException("Option '--backend' must be 'cuda' or 'opencl'.")`), `--device` (`NextInt`; must be ≥ 0), `--list-devices` (flag).

`SearchRunner.ResolveDevice`:

```csharp
    public static bool ResolveDevice(CliOptions options, out IGpuDevice? device, out string reason)
    {
        if (options.Backend is null && options.Device is null)
        {
            return GpuBackends.TryOpenFirst(out device, out reason);
        }

        if (options.Backend is not null)
        {
            return GpuBackends.TryOpen(options.Backend, options.Device ?? 0, out device, out reason);
        }

        // --device without --backend indexes the flat list printed by --list-devices.
        int remaining = options.Device!.Value;
        foreach (IGpuBackend backend in GpuBackends.All)
        {
            int count = backend.EnumerateDevices().Count;
            if (remaining < count)
            {
                return GpuBackends.TryOpen(backend.Name, remaining, out device, out reason);
            }

            remaining -= count;
        }

        device = null;
        reason = $"device index {options.Device} is out of range; run --list-devices";
        return false;
    }
```

`SearchRunner.SearchGpu`: use `ResolveDevice`; throw `InvalidOperationException` with the reason on failure.

`Program.Main`: right after parsing, `if (options.ListDevices) { return ListDevices(); }` where

```csharp
    private static int ListDevices()
    {
        int flat = 0;
        bool any = false;
        foreach (IGpuBackend backend in GpuBackends.All)
        {
            foreach (GpuDeviceInfo info in backend.EnumerateDevices())
            {
                Console.WriteLine($"{flat,2}  {backend.Name}:{info.Index}  {info.Name}  ({info.Details})");
                flat++;
                any = true;
            }
        }

        if (!any)
        {
            Console.Error.WriteLine("no GPU devices found (no CUDA driver and no OpenCL platform).");
            return 2;
        }

        return 0;
    }
```

Replace the `CudaContext? gpuContext` block with `IGpuDevice? gpuDevice = null; if (options.Gpu && !SearchRunner.ResolveDevice(options, out gpuDevice, out string reason)) { Console.Error.WriteLine($"error: --gpu requested but no usable device: {reason}"); return 2; }`; `RunGpu(IGpuDevice device, …)` prints `GPU: {engine.DeviceName}` and its final line; the `catch` covers `CudaException` and `OpenClException` (both `Exception` subclasses — catch each, same message shape `error: GPU search failed: {ex.Message}`, return 2). If the OpenCL program build takes over one second, print `note: compiling the OpenCL program took {seconds:F1} s` (measure around `ResolveDevice` when `Backend == "opencl"` or the resolved device's backend is OpenCL).

README: flags table rows for `--backend`, `--device`, `--list-devices`; a paragraph in the GPU section: backends, preference order, that OpenCL is compiled at run time from the same source, and that any device the CPU reference does not agree with is reported and dropped.

- [ ] **Step 4: Run the suite and smoke both backends**

Run: `-class "*CliOptionsTests"`, `-class "*GpuSearchTests"`, `-class "*GpuCliTests"`, then the whole suite — all green on both backends, warning-free.

Smoke:
```bash
dotnet run --project src/XrplVanity.Cli -c Release -- --list-devices
dotnet run --project src/XrplVanity.Cli -c Release -- --gpu --starts abc --count 2
dotnet run --project src/XrplVanity.Cli -c Release -- --gpu --backend opencl --starts abc --count 2
dotnet run --project src/XrplVanity.Cli -c Release -- --gpu --backend opencl --device 9
```
Expected: the list shows `0  cuda:0 …` and `1  opencl:0 …`; the two searches print two matches each with `consistency errors: 0` and exit 0, their final lines naming `cuda:…` and `opencl:…`; the last command prints the out-of-range error and exits 2. Record both `/s` rates in the commit body.

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.Gpu src/XrplVanity.Cli tests/XrplVanity.Tests README.md
git commit -m "feat(cli): backend and device selection with --backend, --device and --list-devices; engine on the backend abstraction"
```

---

### Task 6: Contributor documentation and per-backend throughput record

**Files:**
- Modify: `README.md` (performance note per backend), `docs/2026-09-03-xrpl-vanity-gpu-opencl-design.md` (append a "Measured" section with the two rates and the OpenCL compile time), `gpu/kernels/xrpl_vanity_body.h` (header comment already points at the design doc — verify)
- Test: none new; the benchmark theory (both backends) is the evidence

- [ ] **Step 1: Record the numbers**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuSearchTests"` and copy the two `GPU search throughput: … on cuda:…` / `… on opencl:…` lines; run `--gpu --backend opencl --starts abc --count 3` and note the compile-time note (if printed) and the final rate.

- [ ] **Step 2: Write them down**

Append to the design doc:

```markdown
## Measured (reference machine, RTX 5070 Ti)

| Backend | Benchmark test (candidates/s) | CLI `--gpu` (candidates/s) | Program compile |
| --- | --- | --- | --- |
| cuda | <value> | <value> | PTX, JIT by driver (< 1 s) |
| opencl | <value> | <value> | <value> s at start-up |
```

(with the actual measured values), and add one sentence to the README GPU section giving both rates.

- [ ] **Step 3: Commit**

```bash
git add README.md docs
git commit -m "docs: record per-backend throughput and the single-source kernel discipline"
```

---

## Notes for the executor

- Task 1 is a pure refactor gated by the full existing suite on CUDA; if any anchor fails after it, the transformation has a slip — compare function by function against the old file in git history (`git show HEAD~1:gpu/kernels/xrpl_vanity.cu`).
- Never fix an OpenCL failure in a test. The body is the single source of truth; the CUDA rows must stay green after every body change (regenerate the PTX).
- Do not add multi-GPU, Linux, or pocl work here (Plans D and E). Do not push, publish, or open a pull request.
