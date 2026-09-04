# XRPL Vanity Generator — Backend Abstraction and OpenCL Backend (Plan C) Design

Status: approved 2026-09-03. Third GPU design document, after
`2026-09-03-xrpl-vanity-gpu-design.md` (CUDA backend, M0–M6, merged). Plan C is the
first of three follow-on sub-projects agreed in this order: **C** backend abstraction +
OpenCL, **D** Linux test harness (Docker; includes a non-NVIDIA OpenCL implementation,
pocl), **E** multi-GPU orchestration. secp256k1 is permanently out of scope for the whole
tool (Ed25519 only).

## Background

The CUDA backend derives ~19M addresses/s on the reference RTX 5070 Ti with every hit
re-verified by the CPU reference. It reaches the GPU through the CUDA driver API only, so
it runs on NVIDIA hardware only. This plan makes the tool run on any OpenCL device while
keeping one — and only one — kernel implementation.

Facts established on the reference machine before this design was written:

- The Khronos ICD loader (`OpenCL.dll`) exposes one platform, "NVIDIA CUDA", OpenCL 3.0
  runtime with **OpenCL C 1.2** device language and **no generic address space**.
- A representative kernel compiled with `-cl-std=CL1.2` and ran correctly: 64-bit
  integer literals via a `ul` suffix, signed `char`, `__constant` pointer parameters,
  functions taking one `__global` and one private pointer, `static inline` functions,
  `atomic_inc` returning unique old values, `clCreateCommandQueue`.
- Only one GPU and no non-NVIDIA device are present. Non-NVIDIA validation is delivered
  by Plan D (pocl, an independent CPU OpenCL implementation, inside a Linux container),
  not by this plan.

## Goals

1. A single kernel source compiled by both backends; correctness on OpenCL established
   by re-running every existing anchor (field ops vs BigInteger, RFC 8032 vectors,
   hashes at padding boundaries, the 1,000,000-input cross-check, the 20,000-address
   matcher equivalence, the search engine gate, windowed == naive).
2. A backend/device abstraction the search engine and the tests use, so that Plan E
   (multi-GPU) and Plan D (Linux, pocl) build on it.
3. `--gpu` keeps working unchanged (CUDA preferred); `--backend` and `--device` allow
   explicit selection; `--list-devices` shows what the machine offers.
4. The CPU re-verification gate is unchanged: nothing is printed that the CPU did not
   reproduce, on any backend.

## Non-goals

- Multi-GPU (Plan E). Linux and pocl (Plan D). Validation on AMD/Intel GPUs (no hardware;
  "expected to work, untested" — the same honesty as Linux in the CUDA design).
- Performance parity between backends. NVIDIA's OpenCL path may be slower than the
  CUDA path; it must be correct, not equal.
- OpenCL 2.x/3.x-only features (generic address space, SVM, pipes, sub-groups). The
  kernel targets OpenCL C 1.2 so that the widest set of devices, including pocl and
  older Intel/AMD runtimes, can compile it.
- Committing OpenCL binaries: OpenCL programs are compiled at run time by the vendor
  driver; the committed artefact for OpenCL is the source text.

## Approach

**One kernel body, two thin shims, two host backends behind one interface.** The body is
written in the intersection of CUDA C++ and OpenCL C 1.2 (a C99 subset). A CUDA shim and
an OpenCL shim provide the type names, qualifiers and helpers that differ. The CUDA build
(`build-ptx.py`, NVRTC) concatenates the CUDA shim and the body and keeps committing the
PTX exactly as today; the OpenCL backend embeds the OpenCL shim + body as text and
compiles it at run time with `clBuildProgram`.

Rejected: a separate OpenCL port (830 lines of cryptography duplicated; every fix twice;
divergence certain) and any CUDA→OpenCL translator (no trustworthy tool).

## Kernel single-source discipline (validated on NVIDIA OpenCL C 1.2)

The body `gpu/kernels/xrpl_vanity_body.h` obeys these rules; the shims
`gpu/kernels/cuda_shim.h` and `gpu/kernels/opencl_shim.h` define the macros.

| Construct | Body writes | CUDA shim | OpenCL shim |
| --- | --- | --- | --- |
| kernel entry | `KERNEL void k_x(GLOBAL const u8* in, GLOBAL u8* out, int n)` | `extern "C" __global__` | `__kernel` |
| device function | `DEVICE void f(...)` / `DEVICE_INLINE` | `__device__` / `__device__ __forceinline__` | `static` / `static inline` |
| thread index | `int i = GLOBAL_ID();` | `blockIdx.x*blockDim.x+threadIdx.x` | `get_global_id(0)` |
| pointer into a device buffer | `GLOBAL const u8* p` | (empty) | `__global` |
| pointer to a program-scope constant table | `CONSTANT const u32* c` | (empty) | `__constant` |
| program-scope constant table | `CONSTANT_TABLE u32 P_LIMBS[8] = {...};` | `__device__ __constant__` | `__constant` |
| 64-bit unsigned | `u64` | `unsigned long long` | `ulong` |
| 64-bit literal | `U64_C(0x428a2f98d728ae22)` | `x##ull` | `x##ul` |
| 8-bit signed | `i8` | `signed char` | `char` |
| atomic slot claim | `ATOMIC_INC(GLOBAL u32* p)` (returns old value) | `atomicAdd(p, 1u)` | `atomic_inc(p)` |
| struct parameters | pointers only: `void fe_add(Fe* r, const Fe* a, const Fe* b)` | — | — |

Rules that follow from "no generic address space" on OpenCL 1.2:

1. **Device functions take private pointers only**, except for the three read-only shared
   tables that are read deep inside the call tree: the base-point `table`, and the matcher's
   `groups` and `chars`. Those parameters are `GLOBAL const` all the way down
   (`scalarmult_base_windowed`, `select_precomp`, `precomp_load`, `derive_address`,
   `matches_any`, `group_has`, `cmp_bytes`'s first argument).
2. **Kernels copy per-thread inputs from `GLOBAL` memory into private arrays before
   calling device functions, and copy results back.** All inputs are small and bounded:
   entropy 16, scalar 32, field element 32, hash inputs ≤ 239, base58 payload 25, PRF key
   32, address record 36. This is a no-op cost on CUDA and the only portable form on
   OpenCL 1.2.
3. No C++: no references, no default arguments, no templates, no `extern "C"` outside the
   shim, no `bool` in kernel signatures (use `int`/`u8`), struct assignment allowed
   (`*r = h;`), `bool` allowed inside device functions.
4. No `unsigned long`, `long`, `size_t` in the body (their widths differ across CUDA-on-Windows
   and OpenCL); only `u8`, `u32`, `u64`, `i8`, `int`, `char`.
5. No `#pragma unroll`, no `__launch_bounds__`, no vendor intrinsics.

Every existing device function keeps its name and semantics; only its parameter passing
changes (references → pointers). The CUDA PTX regenerated from the refactored body must
pass every existing anchor before any OpenCL work starts — the refactor is gated as a
pure no-op on the verified backend.

## Host abstraction

```
XrplVanity.Gpu
  IGpuBackend      Name; IReadOnlyList<GpuDeviceInfo> EnumerateDevices(); IGpuDevice Open(int index)
  GpuDeviceInfo    Backend, Index, Name, Details (record)
  IGpuDevice       Name; Backend; IGpuKernel GetKernel(string name); IGpuBuffer Alloc(long bytes);
                   void Launch(IGpuKernel kernel, int threads, int blockSize, params object[] args);
                   void Synchronize(); IDisposable
  IGpuKernel       (opaque)
  IGpuBuffer       Size; CopyFrom(ReadOnlySpan<byte>); CopyTo(Span<byte>); Clear(); IDisposable
  GpuBackends      static: All (CUDA then OpenCL), FirstAvailableDevice(), Find(backend, index)
```

`Launch(kernel, threads, blockSize, args)` takes the number of threads required; the CUDA
device computes `grid = ceil(threads/blockSize)`, the OpenCL device sets
`global = ceil(threads/blockSize) * blockSize`, `local = blockSize`. Kernels keep their
`if (i >= n) return;` guard. Argument types are `IGpuBuffer`, `ulong`, `long`, `int`,
`uint`, `byte`; each backend marshals them (CUDA: pinned pointer array; OpenCL:
`clSetKernelArg` per index with the value's size, `cl_mem` for buffers).

- `CudaBackend` / `CudaDevice` wrap the existing `CudaContext`, `CudaModule`,
  `DeviceBuffer` (unchanged public API, so the Plan A/B layer stays intact). The device
  loads the embedded PTX.
- `OpenClBackend` / `OpenClDevice`: a P/Invoke layer over the ICD loader (`OpenCL.dll` on
  Windows, `libOpenCL.so.1` on Linux, resolved like the CUDA driver), enumerating every
  platform's devices of `CL_DEVICE_TYPE_ALL` in platform order (CPU devices included, so
  pocl works in Plan D); the device compiles the embedded OpenCL source with
  `-cl-std=CL1.2` on open, and surfaces the build log in the exception when compilation
  fails. Errors carry the `cl_int` code and a name from a small table of the codes this
  layer can hit.
- `GpuSearchEngine` takes an `IGpuDevice` and is otherwise unchanged: table build, target
  upload, launch loop in sub-launches, hit drain, CPU re-verification gate, counters.

## Selection and CLI

- `--gpu` (unchanged): the first available device in backend order CUDA, then OpenCL.
- `--backend cuda|opencl`: restrict to one backend; `--device N`: the N-th device of that
  backend's enumeration (default 0). `--device` without `--backend` indexes the flat list
  printed by `--list-devices`.
- `--list-devices`: prints every backend and device (`cuda:0 NVIDIA GeForce RTX 5070 Ti
  (sm_120)`, `opencl:0 NVIDIA GeForce RTX 5070 Ti (NVIDIA CUDA, OpenCL 3.0 CUDA)`) and
  exits 0; when nothing is available prints that and exits 2.
- Output, `--count`, `--out`, warnings, the secret notice, the consistency-error and
  overflow reporting and the final statistics line are shared and unchanged; the final
  line names the backend and device.
- No backend available with `--gpu` → the existing error, exit 2; no silent CPU fallback.

## Correctness strategy

1. **Refactor gate.** After the body refactor, regenerate the PTX and run the entire
   existing GPU suite on CUDA: it must pass unchanged before any OpenCL code exists.
2. **Both backends, one test body.** Every GPU test that today uses `CudaContext`
   directly is migrated to `IGpuDevice` and parameterised over the available backends
   (`[Theory]` + `[MemberData]` yielding `"cuda"` / `"opencl"`, each skipping when that
   backend is unavailable). The anchors therefore run on both: field ops (BigInteger),
   points (RFC 8032 + BouncyCastle), hashes (padding boundaries, known vectors), base58,
   the entropy→address pipeline (known vector + **1,000,000 inputs**), the device matcher
   (20,000 real addresses), the search engine (verified hits, overflow, fresh entropy,
   `ConsistencyErrors == 0`), windowed == naive, the base-table anchor, throughput floor.
3. **The gate is backend-agnostic** and remains in `GpuSearchEngine`; the CLI's end-to-end
   smoke is run once per backend.
4. **OpenCL build-log visibility.** A compilation failure on any device reports the
   vendor compiler's log — this is how portability problems on devices not present here
   will be diagnosed by users.

## Milestones (one implementation plan; execution by tasks)

| Task | Deliverable | Gate |
| --- | --- | --- |
| 1 | Shims + body refactor (pointers, macros, copy-in/copy-out kernels, literal macro); `build-ptx.py` concatenates shim + body; PTX regenerated | whole existing GPU suite green on CUDA, byte-identical outputs |
| 2 | `IGpuBackend`/`IGpuDevice`/`IGpuBuffer`/`IGpuKernel`, `CudaBackend` adapter, `GpuBackends`; tests migrated to the abstraction (still CUDA-only) | whole suite green |
| 3 | OpenCL P/Invoke layer, `OpenClBackend`/`OpenClDevice`, embedded `.cl` text, runtime compile with build-log errors, first kernel launch on OpenCL | `k_add_arrays` round-trip on OpenCL |
| 4 | Backend parameterisation of every GPU test; all anchors on OpenCL | every anchor green on both backends |
| 5 | `GpuSearchEngine` on `IGpuDevice`; `--backend`, `--device`, `--list-devices`, auto-select; README | end-to-end `--gpu` on CUDA and `--gpu --backend opencl` on this machine |
| 6 | Throughput per backend recorded; documentation of the single-source discipline for contributors | benchmark test passes on both; README/docs updated |

## Risks

- **Compile time**: NVIDIA's OpenCL compiler will unroll the same loops NVRTC does; a
  multi-second `clBuildProgram` at start-up is expected and acceptable (reported once to
  stderr as "compiling OpenCL program…" when it takes over a second).
- **Register pressure / occupancy on OpenCL** may cost throughput versus CUDA on the same
  card; correctness is unaffected and the benchmark floor (500,000/s) stays.
- **Silent semantic differences between compilers** (integer promotion, `char` signedness
  on exotic devices) — mitigated by the full anchor suite on every backend and by the
  runtime CPU gate.
- **Devices not present here** may reject constructs NVIDIA accepts; the build log is
  surfaced verbatim so a user can report it; Plan D's pocl run is the second compiler
  this source is validated against.

## Measured (reference machine, RTX 5070 Ti)

| Backend | Benchmark test (candidates/s) | CLI `--gpu` (candidates/s) | Program compile |
| --- | --- | --- | --- |
| cuda | 19,300,000 | 10,700,000 | PTX, JIT by driver (< 1 s) |
| opencl | 21,000,000 | 12,100,000 | ≈1 s warm (NVIDIA compute cache); 30–74 s cold |

## Assumptions made on the operator's behalf

1. CUDA is preferred over OpenCL when both exist (verified, faster).
2. OpenCL is compiled at run time; nothing binary is committed for it.
3. `--backend`, `--device`, `--list-devices` are flags of the same CLI.
4. OpenCL C 1.2 is the language floor (widest device support, incl. pocl and older
   runtimes); no OpenCL 2.x features are used.
5. Plan order C → D → E; secp256k1 permanently excluded.
6. Windows remains the primary platform; Linux is validated in Plan D.
