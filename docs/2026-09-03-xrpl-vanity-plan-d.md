# Linux Test Harness and pocl Validation — Plan D Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run the whole test suite on Linux in a Docker container — CUDA through GPU passthrough, OpenCL through pocl (an independent non-NVIDIA implementation) — with "the expected backends ran" asserted, and close the three Plan C deferrals that only Linux/pocl justify.

**Architecture:** Test-level assertions (`XRPL_VANITY_REQUIRE_BACKENDS`, CPU-class throughput floor) and host robustness (work-group clamp, PTX/body drift stamp) land first and are verified on Windows. Then a Docker harness — `linux/Dockerfile` on the local `dotnet/sdk:10.0` image plus pocl, `linux/run-linux-tests.sh` on the host, `linux/in-container.sh` inside — runs the suite in `--cpu-only` and GPU modes; whatever pocl surfaces is fixed in the single kernel source and re-verified on both NVIDIA paths.

**Tech Stack:** .NET 10, xUnit v3 4.0.0, Docker Desktop (WSL2 backend, `nvidia` runtime), Ubuntu 24.04 image, pocl 5.0 (`pocl-opencl-icd`), ocl-icd, bash (Git Bash on Windows), Python for `build-ptx.py`.

**Spec:** `docs/2026-09-03-xrpl-vanity-linux-design.md` (and `docs/2026-09-03-xrpl-vanity-gpu-opencl-design.md` for the kernel discipline and the backend abstraction).

## Global Constraints

- `net10.0`; nullable; `XrplVanity.Gpu` has no packages beyond `XrplVanity.Core`; `dotnet build` never requires CUDA or OpenCL.
- Kernel body discipline unchanged (see the Plan C design): pointers only, shim macros, OpenCL C 1.2, no vendor-specific branches; **every body change regenerates the PTX** (`python gpu/build-ptx.py`) and is re-verified on CUDA and NVIDIA OpenCL on Windows (`dotnet run --project tests/XrplVanity.Tests -c Release`, expected 0 skipped on the reference machine).
- Tests run with `dotnet run …` never `dotnet test`; a class: `-- -class "*GpuSearchTests"`. Built-in `Assert`, no FluentAssertions. Build warning-free.
- `XRPL_VANITY_REQUIRE_BACKENDS`: comma-separated backend names, case-insensitive; a listed backend that is unavailable → `Assert.Fail`, otherwise `Assert.Skip`; unset → skip (today's behaviour).
- Throughput floors: 500,000/s for GPU devices, 10,000/s for CPU devices (`IsCpuDevice`).
- Work-group clamp: `local = min(blockSize, CL_KERNEL_WORK_GROUP_SIZE, CL_DEVICE_MAX_WORK_GROUP_SIZE)`, `global = ceil(threads / local) * local`.
- PTX stamp: first line `// body-sha256: <64 hex>`, SHA-256 over `cuda_shim.h + "\n" + xrpl_vanity_body.h` with `\r\n` → `\n` normalised, ASCII bytes; the drift test is a device-free `[Fact]`.
- Line endings: `.gitattributes` pins LF for `*.sh`, `Dockerfile`, `linux/**`, `*.h`, `*.cu`, `*.py`, `*.ptx`; the working tree is renormalised after adding it.
- Harness: image built from the `linux/` directory only; repository bind-mounted read-only at `/src`; sources copied to `/work` excluding `bin`, `obj`, `.git`, `.superpowers`, `docs`; named volumes `xrpl-vanity-nuget` (`/cache/nuget`) and `xrpl-vanity-pocl` (`/cache/pocl`); modes `gpu` (default, `--gpus all`, required `cuda,opencl`) and `cpu` (`--cpu-only`, required `opencl`); smokes with explicit expectations; non-zero exit on any failure.
- Commit locally after each task, Conventional Commits in English, NO `Co-Authored-By` / attribution trailers, do NOT push or publish. Downloads authorised by the operator: apt packages in the image build, NuGet packages into the named volume.

## File Structure

```
.gitattributes                                  # LF pins (Task 1)
gpu/build-ptx.py                                # stamp line (Task 1)
gpu/kernels/xrpl_vanity.ptx                     # regenerated with the stamp (Task 1)
src/XrplVanity.Gpu/XrplVanity.Gpu.csproj        # + embedded cuda_shim.h (Task 1)
src/XrplVanity.Gpu/GpuKernels.cs                # + ReadCudaSource(), ReadPtxBodyHash(), ComputeBodyHash() (Task 1)
src/XrplVanity.Gpu/IGpuBackend.cs               # GpuDeviceInfo.IsCpuDevice, IGpuDevice.IsCpuDevice (Task 1)
src/XrplVanity.Gpu/Cuda/CudaBackend.cs          # IsCpuDevice = false (Task 1)
src/XrplVanity.Gpu/OpenCl/OpenClNative.cs       # + constants, clGetKernelWorkGroupInfo, DeviceUlong/DeviceSize helpers (Task 1)
src/XrplVanity.Gpu/OpenCl/OpenClBackend.cs      # CL_DEVICE_TYPE, work-group clamp (Task 1)
src/XrplVanity.Cli/Program.cs                   # --list-devices [cpu] tag (Task 1)
tests/XrplVanity.Tests/Gpu/GpuTestHelper.cs     # SkipOrFail + REQUIRE_BACKENDS (Task 1)
tests/XrplVanity.Tests/Gpu/GpuBackendsTests.cs  # skips routed through SkipOrFail (Task 1)
tests/XrplVanity.Tests/Gpu/GpuInfrastructureTests.cs  # same (Task 1)
tests/XrplVanity.Tests/Gpu/GpuSearchTests.cs    # CPU-class floor (Task 1)
tests/XrplVanity.Tests/Gpu/KernelSourceTests.cs # drift test (Task 1)
linux/Dockerfile                                # (Task 2)
linux/in-container.sh                           # (Task 2)
linux/run-linux-tests.sh                        # (Task 2)
gpu/kernels/*, src/XrplVanity.Gpu/OpenCl/*      # portability fixes if pocl needs them (Task 2)
README.md, docs/2026-09-03-xrpl-vanity-linux-design.md, docs/2026-09-03-xrpl-vanity-gpu-opencl-design.md  # (Task 4)
```

---

### Task 1: Assertions, robustness and hygiene — verified on Windows

**Files:**
- Create: `.gitattributes`, `tests/XrplVanity.Tests/Gpu/KernelSourceTests.cs`
- Modify: `gpu/build-ptx.py`, `src/XrplVanity.Gpu/XrplVanity.Gpu.csproj`, `src/XrplVanity.Gpu/GpuKernels.cs`, `src/XrplVanity.Gpu/IGpuBackend.cs`, `src/XrplVanity.Gpu/Cuda/CudaBackend.cs`, `src/XrplVanity.Gpu/OpenCl/OpenClNative.cs`, `src/XrplVanity.Gpu/OpenCl/OpenClBackend.cs`, `src/XrplVanity.Cli/Program.cs`, `tests/XrplVanity.Tests/Gpu/GpuTestHelper.cs`, `tests/XrplVanity.Tests/Gpu/GpuBackendsTests.cs`, `tests/XrplVanity.Tests/Gpu/GpuInfrastructureTests.cs`, `tests/XrplVanity.Tests/Gpu/GpuSearchTests.cs`; regenerate `gpu/kernels/xrpl_vanity.ptx`
- Test: `KernelSourceTests` (new), the whole suite

**Interfaces:**
- Consumes: `GpuBackends.TryOpen(backend, 0, out device, out reason)`, `CudaContext.TryCreate`, `OpenClNative.DeviceString`, `OpenClDevice(IntPtr device, GpuDeviceInfo info[, string source])`, `OpenClDevice.Launch`, `GpuKernels.ReadResource(name, hint)`.
- Produces: `GpuDeviceInfo(string Backend, int Index, string Name, string Details, bool IsCpuDevice = false)`; `IGpuDevice.IsCpuDevice`; `GpuTestHelper.SkipOrFail(string backend, string reason)`; `GpuTestHelper.RequiredBackends` (`IReadOnlySet<string>`); `GpuKernels.ReadCudaSource()`, `GpuKernels.ReadPtxBodyHash()`, `GpuKernels.ComputeBodyHash(string cudaSource)`; PTX first line `// body-sha256: …`.

- [ ] **Step 1: Pin line endings and renormalise**

Create `.gitattributes` at the repository root:

```
# Linux executes or hashes these: keep them LF on every checkout.
*.sh        text eol=lf
Dockerfile  text eol=lf
linux/**    text eol=lf
*.h         text eol=lf
*.cu        text eol=lf
*.py        text eol=lf
*.ptx       text eol=lf
```

Then renormalise and re-checkout so the working tree matches:

```bash
cd E:/GIT/XRPL/xrpl-vanity
git add .gitattributes
git add --renormalize .
git status --short | head          # expect only .gitattributes staged (index was already LF)
git ls-files --eol gpu/kernels/opencl_shim.h gpu/build-ptx.py     # i/lf  w/crlf today
git checkout -- gpu/kernels gpu/build-ptx.py                     # re-checkout with the new attributes
git ls-files --eol gpu/kernels/opencl_shim.h gpu/build-ptx.py     # expect i/lf  w/lf
```

If `git checkout -- <paths>` leaves `w/crlf`, delete the files and check them out again (`rm gpu/kernels/*.h gpu/build-ptx.py && git checkout -- gpu/kernels gpu/build-ptx.py`). Note: the PTX regenerated in Step 5 is written by Python in binary mode and is LF already.

- [ ] **Step 2: Device class and work-group clamp**

`src/XrplVanity.Gpu/IGpuBackend.cs`:

```csharp
public sealed record GpuDeviceInfo(string Backend, int Index, string Name, string Details, bool IsCpuDevice = false);
```

and in `IGpuDevice`, after `Name`:

```csharp
    /// <summary>True for a CPU OpenCL device (pocl, Intel/AMD CPU runtimes); false for GPUs.</summary>
    bool IsCpuDevice { get; }
```

`Cuda/CudaBackend.cs`, `CudaDevice`: `public bool IsCpuDevice => false;` (the `GpuDeviceInfo` it builds keeps the default `false`).

`OpenCl/OpenClNative.cs` — add constants and helpers:

```csharp
    internal const uint DeviceType = 0x1000;
    internal const uint DeviceMaxWorkGroupSize = 0x1004;
    internal const uint KernelWorkGroupSize = 0x11B0;
    internal const ulong DeviceTypeCpu = 1UL << 1;

    [DllImport(Lib)] internal static extern int clGetKernelWorkGroupInfo(IntPtr kernel, IntPtr device, uint param, nuint size, out nuint value, out nuint sizeRet);

    internal static ulong DeviceUlong(IntPtr device, uint param)
    {
        byte[] buffer = new byte[8];
        Check(clGetDeviceInfo(device, param, 8, buffer, out _), "clGetDeviceInfo(ulong)");
        return BitConverter.ToUInt64(buffer, 0);
    }

    internal static nuint DeviceSize(IntPtr device, uint param)
    {
        byte[] buffer = new byte[IntPtr.Size];
        Check(clGetDeviceInfo(device, param, (nuint)IntPtr.Size, buffer, out _), "clGetDeviceInfo(size_t)");
        return IntPtr.Size == 8 ? (nuint)BitConverter.ToUInt64(buffer, 0) : (nuint)BitConverter.ToUInt32(buffer, 0);
    }

    internal static nuint KernelMaxWorkGroupSize(IntPtr kernel, IntPtr device)
    {
        Check(clGetKernelWorkGroupInfo(kernel, device, KernelWorkGroupSize, (nuint)IntPtr.Size, out nuint value, out _), "clGetKernelWorkGroupInfo");
        return value;
    }
```

`OpenCl/OpenClBackend.cs`:
- In `TryEnumerate`, after reading `name`/`version`: `bool isCpu = (OpenClNative.DeviceUlong(device, OpenClNative.DeviceType) & OpenClNative.DeviceTypeCpu) != 0;` and construct `new GpuDeviceInfo(BackendName, index, name, $"{platformName}, {version}", isCpu)`.
- `OpenClDevice`: field `private readonly nuint _deviceMaxWorkGroupSize;` set in the 3-argument constructor before `Build` (`_deviceMaxWorkGroupSize = OpenClNative.DeviceSize(device, OpenClNative.DeviceMaxWorkGroupSize);`); `public bool IsCpuDevice { get; }` set from `info.IsCpuDevice`.
- `OpenClKernel`: add `internal nuint MaxWorkGroupSize { get; }` set in `GetKernel` via `OpenClNative.KernelMaxWorkGroupSize(kernel, _device)`.
- `Launch`: replace the two geometry lines with

```csharp
        nuint limit = Math.Min(((OpenClKernel)kernel).MaxWorkGroupSize, _deviceMaxWorkGroupSize);
        if (limit == 0) limit = 1;
        nuint local = Math.Min((nuint)blockSize, limit);
        nuint global = ((nuint)threads + local - 1) / local * local;
```

`src/XrplVanity.Cli/Program.cs`, `ListDevices()`: append ` [cpu]` to the line when `info.IsCpuDevice`:

```csharp
                string tag = info.IsCpuDevice ? " [cpu]" : string.Empty;
                Console.WriteLine($"{flat,2}  {backend.Name}:{info.Index}  {info.Name}  ({info.Details}){tag}");
```

- [ ] **Step 3: Required backends and the CPU-class floor**

`tests/XrplVanity.Tests/Gpu/GpuTestHelper.cs` — add:

```csharp
    /// <summary>Backends named in XRPL_VANITY_REQUIRE_BACKENDS (comma-separated, case-insensitive). A listed backend that is unavailable fails the test instead of skipping it.</summary>
    public static IReadOnlySet<string> RequiredBackends { get; } = ParseRequired(Environment.GetEnvironmentVariable("XRPL_VANITY_REQUIRE_BACKENDS"));

    private static IReadOnlySet<string> ParseRequired(string? value)
    {
        HashSet<string> set = new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
        {
            return set;
        }

        foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            set.Add(part);
        }

        return set;
    }

    /// <summary>Skips the calling test, or fails it when the backend is required by XRPL_VANITY_REQUIRE_BACKENDS.</summary>
    public static void SkipOrFail(string backend, string reason)
    {
        if (RequiredBackends.Contains(backend))
        {
            Assert.Fail($"{backend} is required by XRPL_VANITY_REQUIRE_BACKENDS but unavailable: {reason}");
        }

        Assert.Skip($"{backend} not available: {reason}");
    }
```

and in `RequireDevice` replace both `Assert.Skip(...)` calls: the first with `SkipOrFail(backend, reason);` and the pre-Turing one with `SkipOrFail("cuda", $"CUDA device {probe.DeviceName} is compute capability {probe.ComputeCapabilityMajor}.{probe.ComputeCapabilityMinor}; the PTX targets compute_75 (Turing) or newer");`.

Route the other skips through it: in `GpuBackendsTests` the real-hardware test's `Assert.Skip(...)` when OpenCL has no devices → `GpuTestHelper.SkipOrFail("opencl", "no OpenCL devices");`; in `GpuInfrastructureTests` the CUDA-only fact's skip (`Assert.Skip($"CUDA not available: {reason}")` or similar) → `GpuTestHelper.SkipOrFail("cuda", reason);` and the `Detect()`-based skip → `GpuTestHelper.SkipOrFail("cuda", info)` only if that test is CUDA-specific; if it is backend-agnostic keep `Assert.Skip` (read the test and decide; say which in the report).

`tests/XrplVanity.Tests/Gpu/GpuSearchTests.cs` — rename the throughput test to `Throughput_Meets_The_Floor_For_The_Device_Class` and replace the assertion:

```csharp
        long floor = device.IsCpuDevice ? 10_000 : 500_000;
        _output.WriteLine($"GPU search throughput: {rate:N0} candidates/s on {engine.DeviceName} (floor {floor:N0}/s, {(device.IsCpuDevice ? "CPU" : "GPU")} device)");
        Assert.True(rate >= floor, $"throughput {rate:N0}/s is below the {floor:N0}/s floor for a {(device.IsCpuDevice ? "CPU" : "GPU")} device");
```

(keep the existing `_output.WriteLine` of the raw rate or merge it into this one).

- [ ] **Step 4: PTX/body drift stamp — failing test first**

`src/XrplVanity.Gpu/XrplVanity.Gpu.csproj`: add `<EmbeddedResource Include="..\..\gpu\kernels\cuda_shim.h" LogicalName="cuda_shim.h" />` next to the existing resources.

`src/XrplVanity.Gpu/GpuKernels.cs` — add:

```csharp
    /// <summary>The exact source build-ptx.py hands to NVRTC: cuda_shim.h, a newline, the body.</summary>
    public static string ReadCudaSource() => ReadResource("cuda_shim.h") + "\n" + ReadResource("xrpl_vanity_body.h");

    /// <summary>The SHA-256 stamped into the first line of the embedded PTX by build-ptx.py, or null when absent.</summary>
    public static string? ReadPtxBodyHash()
    {
        string ptx = ReadPtx();
        const string prefix = "// body-sha256: ";
        if (!ptx.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        int end = ptx.IndexOf('\n', prefix.Length);
        string hex = (end < 0 ? ptx[prefix.Length..] : ptx[prefix.Length..end]).Trim();
        return hex.Length == 64 ? hex : null;
    }

    /// <summary>SHA-256 (lower-case hex) of the CUDA source with CRLF normalised to LF — the same value build-ptx.py stamps.</summary>
    public static string ComputeBodyHash(string cudaSource)
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(cudaSource.Replace("\r\n", "\n", StringComparison.Ordinal));
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
    }
```

`tests/XrplVanity.Tests/Gpu/KernelSourceTests.cs` (device-free):

```csharp
using XrplVanity.Gpu;
using Xunit;

namespace XrplVanity.Tests.Gpu;

public sealed class KernelSourceTests
{
    [Fact]
    public void Embedded_Ptx_Is_Stamped_With_The_Hash_Of_The_Embedded_Cuda_Source()
    {
        string? stamped = GpuKernels.ReadPtxBodyHash();
        Assert.NotNull(stamped);

        string actual = GpuKernels.ComputeBodyHash(GpuKernels.ReadCudaSource());
        Assert.True(stamped == actual,
            $"PTX stamp {stamped} != body hash {actual}: the kernel body or cuda_shim.h changed without `python gpu/build-ptx.py` — regenerate and commit the PTX with the body.");
    }

    [Fact]
    public void Body_Hash_Ignores_Line_Ending_Style()
    {
        Assert.Equal(GpuKernels.ComputeBodyHash("a\r\nb\r\n"), GpuKernels.ComputeBodyHash("a\nb\n"));
    }
}
```

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*KernelSourceTests"`
Expected: `Embedded_Ptx_Is_Stamped…` FAILS (no stamp yet: `Assert.NotNull` on null); the line-ending test passes.

- [ ] **Step 5: Stamp the PTX**

`gpu/build-ptx.py`: add `import hashlib` and, where the source is assembled and the PTX written:

```python
    source = shim + b"\n" + body
    body_hash = hashlib.sha256(source.replace(b"\r\n", b"\n")).hexdigest()
```

and

```python
    with open(PTX, "wb") as f:
        f.write(b"// body-sha256: " + body_hash.encode("ascii") + b"\n")
        f.write(ptx.value)
    print(f"wrote {PTX} ({len(ptx.value)} bytes, {ARCH.decode()}, body-sha256 {body_hash[:12]}...)")
```

Regenerate: `python gpu/build-ptx.py` (set `PYTHONIOENCODING=utf-8` if the console mangles output). Verify the first line: `head -1 gpu/kernels/xrpl_vanity.ptx` → `// body-sha256: <64 hex>`; second line the original NVRTC header (`//`).

Run: `-class "*KernelSourceTests"` → both pass. Then `-class "*GpuInfrastructureTests"` (the PTX with a leading comment still loads on CUDA).

- [ ] **Step 6: Whole suite on Windows, both backends**

Run: `dotnet build XrplVanity.sln -c Release` (warning-free) and `dotnet run --project tests/XrplVanity.Tests -c Release`.
Expected: all green, 0 skipped (150 tests: 148 + 2). Then prove the requirement switch: `XRPL_VANITY_REQUIRE_BACKENDS=vulkan dotnet run … -- -class "*GpuInfrastructureTests"` still passes (an unknown required name affects nothing), and `--list-devices` output is unchanged on this machine (no `[cpu]` tag for NVIDIA devices). Record totals.

- [ ] **Step 7: Commit**

```bash
git add .gitattributes gpu src tests
git commit -m "feat(gpu): required-backend assertion, CPU-class throughput floor, work-group clamp and a PTX/body drift stamp"
```

---

### Task 2: Docker harness and first Linux/pocl contact (`--cpu-only`)

**Files:**
- Create: `linux/Dockerfile`, `linux/in-container.sh`, `linux/run-linux-tests.sh`
- Modify (only if pocl surfaces a portability problem): `gpu/kernels/xrpl_vanity_body.h`, `gpu/kernels/opencl_shim.h`, `src/XrplVanity.Gpu/OpenCl/*`; regenerate the PTX after any body change
- Test: the harness run itself

**Interfaces:**
- Consumes: `XRPL_VANITY_REQUIRE_BACKENDS` (Task 1), the CLI (`--list-devices`, `--backend`, `--gpu`), the suite.
- Produces: `bash linux/run-linux-tests.sh [--cpu-only]` with exit code 0 on success; image `xrpl-vanity-linux-tests`; volumes `xrpl-vanity-nuget`, `xrpl-vanity-pocl`.

- [ ] **Step 1: Write the three harness files** (all LF — `.gitattributes` from Task 1 enforces it; create them with an editor that writes LF or run `sed -i 's/\r$//'` afterwards)

`linux/Dockerfile`:

```dockerfile
# Linux test image: the local .NET 10 SDK image (Ubuntu 24.04) plus pocl, an independent
# OpenCL implementation running on the CPU, and the ICD loader. CUDA reaches the container
# through `docker run --gpus all` (the NVIDIA container runtime injects libcuda.so.1).
FROM mcr.microsoft.com/dotnet/sdk:10.0

ENV DEBIAN_FRONTEND=noninteractive \
    NVIDIA_DRIVER_CAPABILITIES=compute,utility \
    POCL_CACHE_DIR=/cache/pocl \
    NUGET_PACKAGES=/cache/nuget \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

RUN apt-get update \
    && apt-get install -y --no-install-recommends ocl-icd-libopencl1 pocl-opencl-icd clinfo \
    && rm -rf /var/lib/apt/lists/*

COPY in-container.sh /usr/local/bin/xrpl-vanity-linux-tests
RUN chmod +x /usr/local/bin/xrpl-vanity-linux-tests

WORKDIR /work
ENTRYPOINT ["/usr/local/bin/xrpl-vanity-linux-tests"]
```

`linux/in-container.sh`:

```bash
#!/usr/bin/env bash
# Runs inside the xrpl-vanity-linux-tests container. Usage: in-container.sh <gpu|cpu>
#   gpu: CUDA (passed-through GPU) and OpenCL (pocl) are both required.
#   cpu: only OpenCL (pocl) is required; CUDA rows skip.
set -euo pipefail

MODE="${1:-gpu}"
case "$MODE" in gpu|cpu) ;; *) echo "usage: $0 <gpu|cpu>" >&2; exit 2 ;; esac

step() { printf '\n== %s\n' "$*"; }
expect_contains() { # expect_contains <label> <needle> <haystack>
    if printf '%s' "$3" | grep -q -- "$2"; then printf 'ok   %s\n' "$1"; else printf 'FAIL %s: expected "%s"\n%s\n' "$1" "$2" "$3"; exit 1; fi
}
expect_exit() { # expect_exit <label> <expected> <actual>
    if [ "$3" -eq "$2" ]; then printf 'ok   %s (exit %s)\n' "$1" "$3"; else printf 'FAIL %s: expected exit %s, got %s\n' "$1" "$2" "$3"; exit 1; fi
}

step "copying sources from /src to /work (excluding bin, obj, .git, .superpowers, docs)"
mkdir -p /work
tar -C /src --exclude=bin --exclude=obj --exclude=.git --exclude=.superpowers --exclude=docs -cf - . | tar -C /work -xf -

step "environment"
clinfo -l || true
if command -v nvidia-smi >/dev/null 2>&1; then nvidia-smi -L || true; else echo "nvidia-smi: not present (no GPU passthrough)"; fi
dotnet --version

step "build"
dotnet build XrplVanity.sln -c Release

if [ "$MODE" = gpu ]; then export XRPL_VANITY_REQUIRE_BACKENDS=cuda,opencl; else export XRPL_VANITY_REQUIRE_BACKENDS=opencl; fi
step "tests (required backends: $XRPL_VANITY_REQUIRE_BACKENDS)"
dotnet run --project tests/XrplVanity.Tests -c Release --no-build

CLI=(dotnet run --project src/XrplVanity.Cli -c Release --no-build --)

step "smoke: --list-devices"
LIST="$("${CLI[@]}" --list-devices)"; printf '%s\n' "$LIST"
expect_contains "list shows opencl:0" "opencl:0" "$LIST"
if [ "$MODE" = gpu ]; then expect_contains "list shows cuda:0" "cuda:0" "$LIST"; fi

step "smoke: --backend opencl"
set +e; OUT="$("${CLI[@]}" --backend opencl --starts ab --count 1 2>&1)"; RC=$?; set -e
expect_exit "opencl search" 0 "$RC"; expect_contains "ran on opencl" "GPU: opencl:" "$OUT"; expect_contains "no consistency errors" "consistency errors: 0" "$OUT"

step "smoke: --gpu (first available backend)"
set +e; OUT="$("${CLI[@]}" --gpu --starts ab --count 1 2>&1)"; RC=$?; set -e
expect_exit "gpu search" 0 "$RC"
if [ "$MODE" = gpu ]; then expect_contains "ran on cuda" "GPU: cuda:" "$OUT"; else expect_contains "fell to opencl (no CUDA)" "GPU: opencl:" "$OUT"; fi

if [ "$MODE" = cpu ]; then
    step "smoke: --backend cuda without a GPU must exit 2"
    set +e; OUT="$("${CLI[@]}" --backend cuda --starts ab --count 1 2>&1)"; RC=$?; set -e
    expect_exit "cuda unavailable" 2 "$RC"; expect_contains "reason printed" "no usable device" "$OUT"
fi

step "done: mode=$MODE, all expectations met"
```

`linux/run-linux-tests.sh`:

```bash
#!/usr/bin/env bash
# Builds the Linux test image and runs the whole suite inside it.
#   bash linux/run-linux-tests.sh            # GPU mode: needs the NVIDIA container runtime; requires cuda AND opencl (pocl)
#   bash linux/run-linux-tests.sh --cpu-only # no GPU: requires opencl (pocl); CUDA rows skip
# Works from Git Bash on Windows (Docker Desktop) and from any Linux shell.
set -euo pipefail

MODE=gpu
if [ "${1:-}" = "--cpu-only" ]; then MODE=cpu; elif [ -n "${1:-}" ]; then echo "usage: $0 [--cpu-only]" >&2; exit 2; fi

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
IMAGE=xrpl-vanity-linux-tests

if command -v cygpath >/dev/null 2>&1; then SRC="$(cygpath -w "$ROOT")"; else SRC="$ROOT"; fi

echo "== building image $IMAGE (context: $ROOT/linux)"
docker build -t "$IMAGE" -f "$ROOT/linux/Dockerfile" "$ROOT/linux"

GPU_ARGS=()
if [ "$MODE" = gpu ]; then GPU_ARGS=(--gpus all); fi

echo "== running mode=$MODE"
MSYS_NO_PATHCONV=1 docker run --rm "${GPU_ARGS[@]}" \
    -v "$SRC:/src:ro" \
    -v xrpl-vanity-nuget:/cache/nuget \
    -v xrpl-vanity-pocl:/cache/pocl \
    "$IMAGE" "$MODE"
```

Make both scripts executable in git: `git update-index --chmod=+x linux/run-linux-tests.sh linux/in-container.sh` (after `git add`).

- [ ] **Step 2: First run — CPU mode**

```bash
cd E:/GIT/XRPL/xrpl-vanity
bash linux/run-linux-tests.sh --cpu-only 2>&1 | tee /tmp/linux-cpu.log
```

Expected on the first run: the image builds (apt downloads pocl + LLVM 17 + ICD, authorised); NuGet restore into the volume; `clinfo -l` lists a "Portable Computing Language" platform with one CPU device; the suite runs with `opencl` required — the `cuda` rows SKIP (allowed in cpu mode), the `opencl` rows run on pocl.

- [ ] **Step 3: Fix what pocl surfaces — at the source**

Three classes of failure, in order of likelihood:
1. **Build failure** — `OpenClException` with pocl's clang log. Fix in `xrpl_vanity_body.h` / `opencl_shim.h` per the Plan C discipline (no pocl-specific branches). Watch-list: the `__constant char B58_ALPHABET[59] = "…"` string initialiser (if rejected, initialise as a brace list of characters — same bytes); `#define DEVICE` (empty) external functions (if warned/rejected, make `DEVICE` = `static` in the OpenCL shim); `atomic_inc` on a non-`volatile` pointer (cast in the `ATOMIC_INC` macro: `atomic_inc((volatile __global unsigned int*)(p))`); `-cl-std=CL1.2` (pocl accepts it).
2. **Launch/runtime failure** — `CL_INVALID_WORK_GROUP_SIZE` (covered by the Task 1 clamp — if it still fires, read `clinfo` for the device's max sizes and report), `CL_OUT_OF_RESOURCES` (private-memory pressure: report which kernel; do not shrink buffers without a controller ruling).
3. **Wrong results** — an anchor fails: bisect with the stage kernels exactly as in Plan A/C (`k_fe_op` op codes, `k_sha512`/`k_sha256`/`k_ripemd160`, `k_scalarmult_base`, `k_base58`) on the failing input; typical suspects are `char`/`i8` signedness and integer promotion of `u8`. Every body change: `python gpu/build-ptx.py` (on Windows), then the Windows suite must stay green on cuda AND opencl before the next Linux run.
4. **Host failure on Linux** (library resolution, `Encoding.ASCII` device strings, the 64 MB build thread) — fix in `src/XrplVanity.Gpu/OpenCl/*` and re-run the Windows suite too.

Repeat Step 2 until the `--cpu-only` run prints `done: mode=cpu, all expectations met` and exits 0. Record in the report: pocl platform/device strings, the first-run compile time (from the `note: compiling the OpenCL program took N s` line of the smokes, or the test wall time), the suite totals line (expect skipped == the number of `cuda` rows, 0 failed), and every fix made.

- [ ] **Step 4: Windows re-verification (if anything changed)**

If any file under `gpu/` or `src/` changed in Step 3: `python gpu/build-ptx.py`, `dotnet run --project tests/XrplVanity.Tests -c Release` on Windows → all green, 0 skipped. If nothing changed, say so.

- [ ] **Step 5: Commit**

```bash
git add linux .gitattributes gpu src tests
git update-index --chmod=+x linux/run-linux-tests.sh linux/in-container.sh
git commit -m "feat(linux): Docker test harness with pocl; the suite runs on Linux in CPU-only mode"
```

(If portability fixes were needed, mention each in the commit body.)

---

### Task 3: GPU mode — CUDA passthrough plus pocl, 0 skipped

**Files:**
- Modify (only if needed): `linux/*`, `src/XrplVanity.Gpu/*`
- Test: the harness run in default mode

**Interfaces:** consumes Task 2's harness; produces the recorded measurements for Task 4.

- [ ] **Step 1: Run the default (GPU) mode**

```bash
cd E:/GIT/XRPL/xrpl-vanity
bash linux/run-linux-tests.sh 2>&1 | tee /tmp/linux-gpu.log
```

Expected: `nvidia-smi -L` lists the RTX 5070 Ti inside the container; the suite runs with `cuda,opencl` required and reports **0 skipped, 0 failed** (CUDA rows on the passed-through GPU through `libcuda.so.1`, OpenCL rows on pocl); `--list-devices` shows `cuda:0 … (sm_120)` and `opencl:0 … [cpu]`; the `--gpu` smoke runs on `cuda:`, the `--backend opencl` smoke on `opencl:`; exit 0.

- [ ] **Step 2: Diagnose any Linux-CUDA-specific failure**

If the CUDA rows fail only on Linux: check the PTX JIT (driver version inside WSL vs the compute_75 PTX), `NativeLibraries` resolution (`libcuda.so.1`), and `CudaContext` device selection; fix in `src/XrplVanity.Gpu/*` and re-verify on Windows. Do not change the kernel for a Linux-only CUDA problem without a controller ruling.

- [ ] **Step 3: Measure on pocl (from the GPU-mode log)**

Record from the log: the throughput test's `GPU search throughput: … on opencl:… (floor 10,000/s, CPU device)` line, the `--backend opencl` smoke's final rate line, the first-run OpenCL compile time (from Task 2's cold run), and the wall time of the 1,000,000-input cross-check on pocl if the runner prints per-test timings (`-diagnostics` may be needed; otherwise time the class: `dotnet run … -- -class "*GpuPipelineTests"` inside the container is not required — take the suite's total time instead).

- [ ] **Step 4: Run `--cpu-only` once more** to confirm both modes pass on the same commit (it must still pass after any Step 2 change).

- [ ] **Step 5: Commit** (only if files changed; otherwise the task's evidence is the report)

```bash
git add linux src
git commit -m "fix(linux): <what the GPU-mode run required>"
```

---

### Task 4: Documentation

**Files:**
- Modify: `README.md` (new "Linux" section after "GPU backends (CUDA and OpenCL)"), `docs/2026-09-03-xrpl-vanity-linux-design.md` (append "## Measured"), `docs/2026-09-03-xrpl-vanity-gpu-opencl-design.md` (Non-goals honesty line)
- Test: none (docs)

- [ ] **Step 1: README "Linux" section** (English, existing style), covering:
  - requirements: Docker (Docker Desktop on Windows or Docker Engine on Linux); for GPU mode the NVIDIA container runtime (Docker Desktop ships it on Windows with the NVIDIA driver; on Linux install the NVIDIA Container Toolkit);
  - the two commands (`bash linux/run-linux-tests.sh`, `bash linux/run-linux-tests.sh --cpu-only`), what each proves, the required-backends assertion (`XRPL_VANITY_REQUIRE_BACKENDS`) and that the CPU-only mode exercises the "no NVIDIA at all" path on pocl;
  - what is downloaded on the first run (apt packages into the image, NuGet packages into the `xrpl-vanity-nuget` volume) and the pocl cache volume;
  - the honesty statement: validated on NVIDIA OpenCL (Windows) and pocl 5.0 (Linux, CPU); AMD/Intel GPUs expected to work, untested; and that a CPU-only CI job is now possible.
  - Update the "GPU backends" section's vendor sentence accordingly and mention `[cpu]` in `--list-devices`.

- [ ] **Step 2: Design docs**

Append to `docs/2026-09-03-xrpl-vanity-linux-design.md`:

```markdown
## Measured (reference machine; container on Docker Desktop / WSL2, 12 vCPU)

| Path | Result |
| --- | --- |
| pocl platform / device | <clinfo strings> |
| OpenCL program compile on pocl (cold / cached) | <s> / <s> |
| Throughput test on pocl (CPU device, floor 10,000/s) | <N>/s |
| CLI `--backend opencl` on pocl | <N>/s |
| Suite wall time, GPU mode / CPU mode | <s> / <s> |
| Portability fixes required by pocl | <none | list> |
```

with the actual values from Tasks 2–3. In `docs/2026-09-03-xrpl-vanity-gpu-opencl-design.md` Non-goals, replace the AMD/Intel sentence with: "Validation on AMD/Intel GPUs (no hardware; validated on NVIDIA OpenCL and on pocl 5.0 — an independent CPU implementation — by Plan D; AMD/Intel expected to work, untested)."

- [ ] **Step 3: Commit**

```bash
git add README.md docs
git commit -m "docs: Linux harness, pocl validation results and the updated vendor statement"
```

---

## Notes for the executor

- Task 1 is verified entirely on Windows; nothing Linux-related runs before Task 2.
- Never fix a pocl failure in a test or with a pocl-specific branch; the body/shim/host is the single source of truth and CUDA + NVIDIA OpenCL must stay green after every change (regenerate the PTX; the drift test catches a forgotten regeneration).
- The harness must be runnable by a stranger from a clean clone: no absolute paths, no assumptions about the host beyond Docker (+ NVIDIA runtime for GPU mode).
- Do not push, publish, or open a pull request.
