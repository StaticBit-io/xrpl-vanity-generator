# XRPL Vanity Generator — Linux Test Harness and pocl Validation (Plan D) Design

Status: approved 2026-09-03. Fourth GPU design document, after the CUDA backend (Plan A/B,
`2026-09-03-xrpl-vanity-gpu-design.md`) and the backend abstraction + OpenCL backend
(Plan C, `2026-09-03-xrpl-vanity-gpu-opencl-design.md`, merged). Plan D is the second of the
three follow-on sub-projects (**C** → **D** → **E** multi-GPU). secp256k1 remains permanently
out of scope.

## Background

Plan C made the GPU pipeline run on OpenCL from the same kernel source as CUDA, but every
OpenCL result so far comes from **one implementation on one platform**: NVIDIA's OpenCL on
Windows. The tool's Linux support (`libcuda.so.1`, `libOpenCL.so.1` resolvers) has never
been executed, and no non-NVIDIA OpenCL compiler has ever seen the kernel. Plan D closes both
gaps with one harness.

Facts established on the reference machine before this design was written:

- Docker Desktop (Linux engine on WSL2, kernel 6.6) has the `nvidia` container runtime
  registered; `docker run --gpus all` injects `libcuda.so.1` and `nvidia-smi` sees the
  RTX 5070 Ti inside a container.
- WSL2 exposes CUDA but **not** NVIDIA OpenCL: the WSL driver library directory contains
  no OpenCL library, and a container with `--gpus all` has no `/etc/OpenCL/vendors`.
  Inside a Linux container on this machine the only OpenCL platform will therefore be the
  one we install ourselves.
- `mcr.microsoft.com/dotnet/sdk:10.0` (Ubuntu 24.04 "noble", .NET SDK 10.0.302) is already
  present locally; Ubuntu 24.04 packages `pocl-opencl-icd` 5.0 (OpenCL 3.0 runtime,
  OpenCL C 1.2 device language, LLVM 17), `ocl-icd-libopencl1` and `clinfo`.
- The repository has no `.gitattributes` and the machine has `core.autocrlf=true`: every
  file in the working tree is CRLF. A CRLF shell script or Dockerfile breaks on Linux, and a
  hash over kernel source would differ between Windows and Linux checkouts.
- Only one GPU, no AMD/Intel device. pocl (an independent implementation from a different
  vendor, running on the CPU) is the non-NVIDIA validation this project can obtain.

## Goals

1. **Run the whole suite on Linux**, twice: with the GPU passed through (CUDA rows on the
   RTX 5070 Ti, OpenCL rows on pocl) and without it (`--cpu-only`: CUDA rows skipped by
   requirement, OpenCL rows on pocl) — the second is the "machine without NVIDIA" scenario
   nothing has ever exercised.
2. **Validate the single-source kernel on a second OpenCL implementation** (pocl 5.0,
   LLVM-based): every anchor (field ops, hashes, RFC 8032, windowed == naive, the
   20,000-address matcher equivalence, the 1,000,000-input cross-check, the engine gate)
   green on pocl.
3. **Make "the expected backends actually ran" an assertion, not an inspection** — a
   required-but-unavailable backend fails the run instead of skipping.
4. Close the three Plan C deferrals that only Linux/pocl can justify: the work-group-size
   clamp, the PTX/body drift guard, and the CPU-device throughput floor.
5. Documentation: how to run the harness, what it proves, and the updated honesty
   statement (validated on NVIDIA OpenCL and pocl; AMD/Intel GPUs expected to work,
   untested).

## Non-goals

- Multi-GPU (Plan E). A CI workflow (the repository is unpublished; note that the pocl
  path makes a **CPU-only CI job** possible later without any GPU runner).
- NVIDIA OpenCL under WSL2 (not provided by WSL). Native Linux installation instructions
  beyond the container. Validation on AMD/Intel hardware.
- Performance on pocl: a CPU OpenCL device is a correctness oracle here, not a search
  backend; its rate is recorded, not optimised.
- pocl-specific branches in the kernel or the host: every portability fix goes into the
  single source and is re-verified on CUDA and NVIDIA OpenCL.

## Approach

**A Docker harness on the already-present .NET SDK image.** `linux/Dockerfile` adds pocl,
the ICD loader and `clinfo`; `linux/run-linux-tests.sh` (bash; runs from Git Bash on
Windows and from any Linux shell) builds the image, starts the container with the
repository bind-mounted read-only and two named volumes (NuGet packages, pocl kernel
cache), and `linux/in-container.sh` copies the sources out of the mount (so Windows
`bin/obj` never mix with Linux builds), builds, runs the suite with the required-backend
assertion set for the mode, and runs the CLI smokes.

Rejected: installing a WSL2 distribution and running natively (a system change, no
isolation, and still no NVIDIA OpenCL); GitHub Actions (unpublished repository, no GPU
runners — deferred, see Non-goals).

## Components

### Repository hygiene (prerequisite)

`.gitattributes` pins LF for everything that Linux executes or hashes:

```
*.sh        text eol=lf
Dockerfile  text eol=lf
linux/**    text eol=lf
*.h         text eol=lf
*.cu        text eol=lf
*.py        text eol=lf
*.ptx       text eol=lf
```

After adding it the affected files are re-normalised (`git add --renormalize .`) and
re-checked out so the working tree is LF for them. Everything else keeps the platform
default.

### Test-level assertions

- `XRPL_VANITY_REQUIRE_BACKENDS` (comma-separated, case-insensitive; e.g. `cuda,opencl`):
  every place a GPU test skips for an unavailable backend goes through one helper,
  `GpuTestHelper.SkipOrFail(backend, reason)`, which calls `Assert.Fail` when the backend is
  listed and `Assert.Skip` otherwise. Unset → today's behaviour. This is what makes "0
  skipped" a checked property of a harness run.
- `IGpuDevice.IsCpuDevice` and `GpuDeviceInfo.IsCpuDevice` (CUDA: false; OpenCL: from
  `CL_DEVICE_TYPE`). The throughput test keeps its 500,000/s floor for GPU devices and uses
  a 10,000/s floor for CPU devices; `--list-devices` tags CPU devices with `[cpu]`.

### Host robustness (Plan C deferrals)

- **Work-group clamp**: `OpenClDevice` caches `CL_DEVICE_MAX_WORK_GROUP_SIZE` and, per
  kernel, `CL_KERNEL_WORK_GROUP_SIZE` (`clGetKernelWorkGroupInfo`); a launch uses
  `local = min(blockSize, kernelMax, deviceMax)` and `global = ceil(threads/local) * local`.
  Kernels only use the global id, so any local size is correct.
- **PTX/body drift guard**: `build-ptx.py` writes `// body-sha256: <hex>` as the first line
  of the PTX, the hash taken over the exact source handed to NVRTC (`cuda_shim.h` + `"\n"` +
  body) with CRLF normalised to LF. `cuda_shim.h` becomes an embedded resource,
  `GpuKernels.ReadCudaSource()` reproduces the same string, and a device-free `[Fact]`
  recomputes the hash and compares it with the stamp — so an edited body with a stale PTX
  fails on every machine, GPU or not. PTX permits `//` comments before `.version` (NVRTC's
  own output starts with them).

### The harness

- `linux/Dockerfile` (build context: the `linux/` directory only):
  `FROM mcr.microsoft.com/dotnet/sdk:10.0`; `apt-get install ocl-icd-libopencl1
  pocl-opencl-icd clinfo`; `ENV NVIDIA_DRIVER_CAPABILITIES=compute,utility
  POCL_CACHE_DIR=/cache/pocl NUGET_PACKAGES=/cache/nuget DOTNET_CLI_TELEMETRY_OPTOUT=1`;
  copies `in-container.sh` as the entrypoint.
- `linux/run-linux-tests.sh [--cpu-only]` (host): builds the image, converts the repository
  path for Docker Desktop on Windows (`cygpath -w` when present, `MSYS_NO_PATHCONV=1`), runs
  the container with `--gpus all` unless `--cpu-only`, `-v <repo>:/src:ro`, named volumes
  `xrpl-vanity-nuget` and `xrpl-vanity-pocl`, and passes the mode (`gpu` | `cpu`) to the
  entrypoint. Exit code = the container's.
- `linux/in-container.sh <gpu|cpu>`: `set -euo pipefail`; copies `/src` to `/work`
  excluding `bin`, `obj`, `.git`, `.superpowers`, `docs`; prints `clinfo -l` and
  `nvidia-smi -L` (when present) for the record; `dotnet build XrplVanity.sln -c Release`;
  exports `XRPL_VANITY_REQUIRE_BACKENDS=cuda,opencl` (gpu) or `opencl` (cpu); runs the suite
  with `dotnet run --project tests/XrplVanity.Tests -c Release --no-build`; then the
  smokes with explicit expectations:
  - `--list-devices` contains `opencl:0` and (gpu mode) `cuda:0`;
  - `--backend opencl --starts ab --count 1` exits 0 and reports `GPU: opencl:`;
  - gpu mode: `--gpu --starts ab --count 1` reports `GPU: cuda:`; cpu mode: the same
    command reports `GPU: opencl:` (first available backend) and `--backend cuda` exits 2;
  - prints a one-line summary per step. Any failed expectation exits non-zero.

### Timing expectations

pocl compiles the ~1,150-line program with LLVM: tens of seconds cold, then served from
the pocl cache volume (keyed by source + options) — later device opens are fast. The
1,000,000-input cross-check on a 12-vCPU CPU device is expected in the tens of seconds; no
TDR exists on Linux, sub-launch sizes stay as they are. Measured values are recorded in the
design doc's "Measured" section when the harness first runs green.

## Correctness strategy

1. Every code change in this plan is first verified on Windows against both existing
   backends (the suite stays 0-skipped there; the drift test is device-free).
2. First Linux contact is `--cpu-only`: whatever pocl's compiler or runtime rejects is
   fixed in the single source (body/shim/host) and re-verified on CUDA and NVIDIA OpenCL
   before the next Linux run; the PTX is regenerated with every body change. The Plan C
   final review's portability watch-list is the diagnostic checklist: `local_work_size`,
   private-memory pressure, the `__constant char[]` string initialiser, `static` functions,
   `char` signedness, `atomic_inc` argument qualifiers, `-cl-std=CL1.2`,
   `clCreateCommandQueue`, `clEnqueueFillBuffer`, vendor strings, the 64 MB build thread.
3. Then the GPU mode: CUDA rows on the passed-through RTX inside the container, OpenCL
   rows on pocl, required backends `cuda,opencl`, 0 skipped.
4. The CPU re-verification gate is unchanged and applies to pocl like any device.

## Milestones (one implementation plan)

| Task | Deliverable | Gate |
| --- | --- | --- |
| 1 | `.gitattributes` + renormalisation; `IsCpuDevice`; work-group clamp; `XRPL_VANITY_REQUIRE_BACKENDS`; CPU-class floor; PTX drift stamp + test | Windows suite green on both backends, drift test green |
| 2 | Dockerfile, run script, in-container script; first `--cpu-only` run; portability fixes surfaced by pocl | `--cpu-only` run green with `opencl` required |
| 3 | GPU mode run; smokes; measurements on pocl | default run green with `cuda,opencl` required, 0 skipped |
| 4 | README "Linux" section; honesty statements; "Measured" section | docs match the harness behaviour |

## Risks

- **pocl rejects or miscompiles a construct** NVIDIA accepts — the point of the exercise;
  fixed at the source and re-verified on both NVIDIA paths.
- **pocl throughput too low for the 1,000,000-input cross-check** to be practical: the
  batch loop is unchanged (65,536 per batch); if a run exceeds a few minutes the test size
  stays and the harness timeout is raised — the anchor is not weakened.
- **Docker Desktop path handling on Windows** (Git Bash path conversion) — handled by
  `cygpath` + `MSYS_NO_PATHCONV`; on Linux hosts the same script runs unchanged.
- **CRLF** — pinned by `.gitattributes`; the drift hash normalises line endings.

## Assumptions made on the operator's behalf

1. Docker harness, not a WSL distribution; one bash script, no PowerShell twin.
2. pocl is the only OpenCL runtime in the container; CUDA comes through WSL2 passthrough.
3. CPU-device throughput floor 10,000/s; both floors live in the test, not in the script.
4. Named volumes for NuGet and the pocl cache (the operator authorised the downloads:
   apt packages ≈150–200 MB, NuGet packages for the Linux restore).
5. Build context for the image is `linux/` only; sources are bind-mounted at run time and
   copied inside the container, never built in place.
6. Plan order C → D → E; secp256k1 permanently excluded.

## Measured (reference machine; container on Docker Desktop / WSL2, 12 vCPU)

| Path | Result |
| --- | --- |
| pocl platform / device | Portable Computing Language / cpu-skylake-avx512-11th Gen Intel(R) Core(TM) i5-11400F @ 2.60GHz |
| OpenCL program compile on pocl (cold / cached) | not measured separately; the pocl kernel cache lives in the `xrpl-vanity-pocl` volume |
| Throughput test on pocl (CPU device, floor 10,000/s) | 56,000/s |
| CLI `--backend opencl` on pocl | 58,000/s |
| Suite wall time, GPU mode / CPU mode | 273.083 s / 207.139 s |
| Portability fixes required by pocl | OpenCL setup serialised process-wide (SetupLock) for thread-safety; no kernel change needed |
| pocl LLVM version | LLVM 16 (via apt package pocl 5.0-2.1build3 on Ubuntu 24.04) |
