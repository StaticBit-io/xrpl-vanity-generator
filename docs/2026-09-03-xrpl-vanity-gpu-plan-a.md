# GPU Backend — Plan A (M0–M4: the math is proven) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build and *prove correct* a CUDA kernel that derives XRPL Ed25519 addresses from 16-byte entropy, driven from .NET through the CUDA driver API, verified against RFC 8032 vectors and the CPU reference over 1,000,000 inputs.

**Architecture:** `XrplVanity.Gpu` (.NET) P/Invokes `nvcuda.dll`/`libcuda.so.1` directly and loads a PTX embedded as a resource; the PTX is generated at development time from one device-only file `gpu/kernels/xrpl_vanity.cu` by `gpu/build-ptx.py` using NVRTC (no host C++ compiler). The kernel grows task by task — field arithmetic → point arithmetic → hashes → full pipeline — and every stage exposes a test entry point checked against an independent oracle (`BigInteger`, RFC 8032, .NET/BouncyCastle, `BouncyCastleDeriver`). Search, matching and the CLI flag are Plan B (M5–M6).

**Tech Stack:** .NET 10, C# latest, CUDA driver API via P/Invoke, NVRTC 13.x (pip wheel `nvidia-cuda-nvrtc`) for PTX generation, PTX target `compute_75`, xUnit v3 4.0.0, `System.Numerics.BigInteger`, BouncyCastle.Cryptography 2.7.0 (via Core).

**Spec:** `docs/2026-09-03-xrpl-vanity-gpu-design.md` (and, for the CPU reference, `docs/2026-09-03-xrpl-vanity-design.md`)

## Global Constraints

- Target `net10.0`; `<Nullable>enable</Nullable>`; `<LangVersion>latest</LangVersion>`; `XrplVanity.Gpu` has `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`; no package dependencies beyond `XrplVanity.Core`.
- `dotnet build` must NEVER require CUDA: P/Invoke resolves at runtime; the PTX is a committed text file embedded as a resource.
- PTX target is `compute_75`. Regenerate with `python gpu/build-ptx.py` after every change to `gpu/kernels/xrpl_vanity.cu`, and commit the `.cu` and `.ptx` together.
- NVRTC location: the pip wheel at `%APPDATA%\Python\Python314\site-packages\nvidia\cu13\bin\x86_64` (Windows user site-packages); `gpu/build-ptx.py` finds it via `site.getusersitepackages()` or the `NVRTC_DIR` environment variable.
- Driver API entry points with `_v2` suffixes MUST be used where they exist (`cuCtxCreate_v2`, `cuCtxDestroy_v2`, `cuMemAlloc_v2`, `cuMemFree_v2`, `cuMemcpyHtoD_v2`, `cuMemcpyDtoH_v2`, `cuMemsetD8_v2`): the un-suffixed exports are legacy 32-bit-size variants.
- Tests: xUnit v3 4.0.0, built-in `Assert`, NO FluentAssertions. GPU tests call `GpuTestHelper.RequireContext()` which `Assert.Skip`s when CUDA is unavailable, so the suite stays green on machines without a GPU.
- **Run tests with `dotnet run`, never `dotnet test`**: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuFieldTests"`; whole suite by omitting `-class`. Build must be warning-free.
- All field elements handed between device functions are **canonical** (fully reduced, `< p`); every public field op reduces its output. Performance work is Plan B/M6, never this plan.
- Commit locally after each task with a plain Conventional Commit message. Do NOT add `Co-Authored-By` or any AI attribution trailer. Do NOT push or publish.
- All artifacts in English.
- Constants (verified with an independent Python reference against RFC 8032 §7.1 TEST 1–3):
  - `p = 2^255 - 19`; limbs LE `{0xffffffed,0xffffffff,0xffffffff,0xffffffff,0xffffffff,0xffffffff,0xffffffff,0x7fffffff}`
  - `p - 2` limbs LE `{0xffffffeb,0xffffffff,0xffffffff,0xffffffff,0xffffffff,0xffffffff,0xffffffff,0x7fffffff}`
  - `2d mod p` limbs LE `{0x26b2f159,0xebd69b94,0x8283b156,0x00e0149a,0xeef3d130,0x198e80f2,0x56dffce7,0x2406d9dc}`
  - `Bx` limbs LE `{0x8f25d51a,0xc9562d60,0x9525a7b2,0x692cc760,0xfdd6dc5c,0xc0a4e231,0xcd6e53fe,0x216936d3}`
  - `By` limbs LE `{0x66666658,0x66666666,0x66666666,0x66666666,0x66666666,0x66666666,0x66666666,0x66666666}`
  - `Bx*By mod p` limbs LE `{0xa5b7dda3,0x6dde8ab3,0x775152f5,0x20f09f80,0x64abe37d,0x66ea4e8e,0xd78b7665,0x67875f0f}`
  - RFC 8032 TEST 1: seed `9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60` → public `d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a`
  - RFC 8032 TEST 2: seed `4ccd089b28ff96da9db6c346ec114e0f5b8a319f35aba624da8cf6ed4fb8a6fb` → public `3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c`
  - RFC 8032 TEST 3: seed `c5aa8df43f9f837bedb7442f31dcb7b166d38535076f094b85ce3a2e0b4458f7` → public `fc51cd8e6218a1a38da47ed00230f0580816ed13ba3303ac5deb911548908025`
  - CPU known vector: seed `sEdTuRgwYiEcubPJfRJUArVyn7AXkVj` → address `rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK`

## File Structure

```
src/XrplVanity.Gpu/
  XrplVanity.Gpu.csproj      # net10, refs Core, embeds ../../gpu/kernels/xrpl_vanity.ptx (from Task 2)
  CudaDriver.cs              # raw P/Invoke surface + library resolver (nvcuda / libcuda.so.1)
  CudaException.cs           # error type carrying CUresult + cuGetErrorString text
  CudaContext.cs             # init/device/context; LoadModule; Alloc; Launch; Synchronize; Dispose
  CudaModule.cs              # module handle; GetFunction
  DeviceBuffer.cs            # device allocation; CopyFrom/CopyTo spans; Dispose
  GpuAvailability.cs         # Detect() -> (Available, Info) without throwing
  GpuKernels.cs              # ReadPtx() from the embedded resource (from Task 2)
gpu/kernels/xrpl_vanity.cu   # the one device-only kernel source (grows Task 2 -> 6)
gpu/kernels/xrpl_vanity.ptx  # generated by build-ptx.py; committed
gpu/build-ptx.py             # NVRTC -> PTX, compute_75
tests/XrplVanity.Tests/Gpu/
  GpuTestHelper.cs           # RequireContext() with Assert.Skip; limb/BigInteger helpers
  GpuInfrastructureTests.cs  # Tasks 1-2
  GpuFieldTests.cs           # Task 3 (M1)
  GpuPointTests.cs           # Task 4 (M2)
  GpuHashTests.cs            # Task 5 (M3)
  GpuPipelineTests.cs        # Task 6 (M4)
```

---

### Task 1: `XrplVanity.Gpu` project and the CUDA driver layer

**Files:**
- Create: `src/XrplVanity.Gpu/XrplVanity.Gpu.csproj`, `CudaDriver.cs`, `CudaException.cs`, `CudaContext.cs`, `CudaModule.cs`, `DeviceBuffer.cs`, `GpuAvailability.cs`
- Modify: `XrplVanity.sln` (add project); `tests/XrplVanity.Tests/XrplVanity.Tests.csproj` (reference Gpu)
- Test: `tests/XrplVanity.Tests/Gpu/GpuTestHelper.cs`, `tests/XrplVanity.Tests/Gpu/GpuInfrastructureTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (namespace `XrplVanity.Gpu`):
  - `sealed class CudaContext : IDisposable` — `static bool TryCreate(out CudaContext? context, out string reason)`; `string DeviceName`; `int ComputeCapabilityMajor`, `int ComputeCapabilityMinor`; `CudaModule LoadModule(string ptx)`; `DeviceBuffer Alloc(long bytes)`; `void Launch(CudaFunction function, int gridX, int blockX, params object[] args)` (args: `DeviceBuffer`, `ulong`, `long`, `int`, `uint`, `byte`); `void Synchronize()`.
  - `sealed class CudaModule` — `CudaFunction GetFunction(string name)`.
  - `sealed class CudaFunction` (opaque).
  - `sealed class DeviceBuffer : IDisposable` — `ulong Pointer`; `long Size`; `void CopyFrom(ReadOnlySpan<byte> source)`; `void CopyTo(Span<byte> destination)`; `void Clear()`.
  - `static class GpuAvailability` — `(bool Available, string Info) Detect()`.
  - `sealed class CudaException : Exception` — `int Result`.
  - Test helper `XrplVanity.Tests.Gpu.GpuTestHelper.RequireContext() -> CudaContext` (skips when unavailable).

- [ ] **Step 1: Create the project and wire references**

```bash
cd E:/GIT/XRPL/xrpl-vanity
dotnet new classlib -n XrplVanity.Gpu -o src/XrplVanity.Gpu -f net10.0
rm -f src/XrplVanity.Gpu/Class1.cs
dotnet sln add src/XrplVanity.Gpu/XrplVanity.Gpu.csproj
dotnet add src/XrplVanity.Gpu/XrplVanity.Gpu.csproj reference src/XrplVanity.Core/XrplVanity.Core.csproj
dotnet add tests/XrplVanity.Tests/XrplVanity.Tests.csproj reference src/XrplVanity.Gpu/XrplVanity.Gpu.csproj
mkdir -p tests/XrplVanity.Tests/Gpu
```

Replace `src/XrplVanity.Gpu/XrplVanity.Gpu.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ImplicitUsings>disable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\XrplVanity.Core\XrplVanity.Core.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write the failing test**

`tests/XrplVanity.Tests/Gpu/GpuTestHelper.cs`:

```csharp
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using XrplVanity.Gpu;
using Xunit;

namespace XrplVanity.Tests.Gpu;

internal static class GpuTestHelper
{
    /// <summary>Returns a live CUDA context, or skips the calling test when CUDA is unavailable.</summary>
    public static CudaContext RequireContext()
    {
        if (!CudaContext.TryCreate(out CudaContext? context, out string reason))
        {
            Assert.Skip($"CUDA not available: {reason}");
        }

        return context!;
    }

    public static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;

    /// <summary>8 little-endian 32-bit limbs -> BigInteger.</summary>
    public static BigInteger FromLimbs(ReadOnlySpan<uint> limbs)
    {
        byte[] bytes = new byte[32];
        MemoryMarshal.AsBytes(limbs).CopyTo(bytes);
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
    }

    /// <summary>BigInteger (0 <= v < 2^256) -> 8 little-endian 32-bit limbs.</summary>
    public static uint[] ToLimbs(BigInteger value)
    {
        byte[] bytes = new byte[32];
        byte[] raw = value.ToByteArray(isUnsigned: true, isBigEndian: false);
        Array.Copy(raw, bytes, Math.Min(raw.Length, 32));
        uint[] limbs = new uint[8];
        MemoryMarshal.AsBytes<uint>(limbs).Clear();
        bytes.AsSpan().CopyTo(MemoryMarshal.AsBytes<uint>(limbs));
        return limbs;
    }

    public static byte[] AsBytes(uint[] limbs) => MemoryMarshal.AsBytes<uint>(limbs).ToArray();

    public static uint[] AsUInts(ReadOnlySpan<byte> bytes) => MemoryMarshal.Cast<byte, uint>(bytes).ToArray();
}
```

`tests/XrplVanity.Tests/Gpu/GpuInfrastructureTests.cs`:

```csharp
using XrplVanity.Gpu;
using Xunit;

namespace XrplVanity.Tests.Gpu;

public class GpuInfrastructureTests
{
    [Fact]
    public void Detect_Reports_A_Device_Or_A_Reason()
    {
        (bool available, string info) = GpuAvailability.Detect();

        Assert.False(string.IsNullOrWhiteSpace(info));
        if (!available)
        {
            Assert.Skip($"CUDA not available: {info}");
        }
    }

    [Fact]
    public void Context_Exposes_Device_Name_And_Compute_Capability()
    {
        using CudaContext context = GpuTestHelper.RequireContext();

        Assert.False(string.IsNullOrWhiteSpace(context.DeviceName));
        Assert.True(context.ComputeCapabilityMajor >= 7); // Turing (7.5) or newer per the spec
    }

    [Fact]
    public void Device_Buffer_Roundtrips_Bytes()
    {
        using CudaContext context = GpuTestHelper.RequireContext();
        byte[] data = new byte[4096];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i * 7);
        }

        using DeviceBuffer buffer = context.Alloc(data.Length);
        buffer.CopyFrom(data);
        byte[] back = new byte[data.Length];
        buffer.CopyTo(back);

        Assert.Equal(data, back);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuInfrastructureTests"`
Expected: build error — `CudaContext`, `DeviceBuffer`, `GpuAvailability` do not exist.

- [ ] **Step 4: Write the driver layer**

`src/XrplVanity.Gpu/CudaException.cs`:

```csharp
using System;

namespace XrplVanity.Gpu;

public sealed class CudaException : Exception
{
    public CudaException(int result, string operation, string message)
        : base($"{operation} failed with CUDA error {result}: {message}")
    {
        Result = result;
    }

    public int Result { get; }
}
```

`src/XrplVanity.Gpu/CudaDriver.cs`:

```csharp
using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace XrplVanity.Gpu;

/// <summary>
/// Minimal CUDA driver API surface. The library name "cuda" is resolved at runtime to
/// nvcuda.dll (Windows) or libcuda.so.1 (Linux), so the assembly loads on machines
/// without any NVIDIA driver; calls fail only when actually made.
/// </summary>
internal static class CudaDriver
{
    private const string Lib = "cuda";

    internal const int CudaSuccess = 0;
    internal const int AttributeComputeCapabilityMajor = 75;
    internal const int AttributeComputeCapabilityMinor = 76;

    static CudaDriver()
    {
        NativeLibrary.SetDllImportResolver(typeof(CudaDriver).Assembly, Resolve);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != Lib)
        {
            return IntPtr.Zero;
        }

        string native = OperatingSystem.IsWindows() ? "nvcuda.dll" : "libcuda.so.1";
        return NativeLibrary.TryLoad(native, out IntPtr handle) ? handle : IntPtr.Zero;
    }

    [DllImport(Lib)] internal static extern int cuInit(uint flags);
    [DllImport(Lib)] internal static extern int cuDeviceGetCount(out int count);
    [DllImport(Lib)] internal static extern int cuDeviceGet(out int device, int ordinal);
    [DllImport(Lib)] internal static extern int cuDeviceGetName([Out] byte[] name, int length, int device);
    [DllImport(Lib)] internal static extern int cuDeviceGetAttribute(out int value, int attribute, int device);
    [DllImport(Lib, EntryPoint = "cuCtxCreate_v2")] internal static extern int cuCtxCreate(out IntPtr context, uint flags, int device);
    [DllImport(Lib, EntryPoint = "cuCtxDestroy_v2")] internal static extern int cuCtxDestroy(IntPtr context);
    [DllImport(Lib)] internal static extern int cuModuleLoadDataEx(out IntPtr module, byte[] image, uint numOptions, IntPtr options, IntPtr optionValues);
    [DllImport(Lib)] internal static extern int cuModuleGetFunction(out IntPtr function, IntPtr module, [MarshalAs(UnmanagedType.LPStr)] string name);
    [DllImport(Lib, EntryPoint = "cuMemAlloc_v2")] internal static extern int cuMemAlloc(out ulong pointer, nuint bytes);
    [DllImport(Lib, EntryPoint = "cuMemFree_v2")] internal static extern int cuMemFree(ulong pointer);
    [DllImport(Lib, EntryPoint = "cuMemcpyHtoD_v2")] internal static extern int cuMemcpyHtoD(ulong destination, IntPtr source, nuint bytes);
    [DllImport(Lib, EntryPoint = "cuMemcpyDtoH_v2")] internal static extern int cuMemcpyDtoH(IntPtr destination, ulong source, nuint bytes);
    [DllImport(Lib, EntryPoint = "cuMemsetD8_v2")] internal static extern int cuMemsetD8(ulong destination, byte value, nuint count);
    [DllImport(Lib)] internal static extern int cuLaunchKernel(IntPtr function, uint gridX, uint gridY, uint gridZ, uint blockX, uint blockY, uint blockZ, uint sharedMemBytes, IntPtr stream, IntPtr[] kernelParams, IntPtr extra);
    [DllImport(Lib)] internal static extern int cuCtxSynchronize();
    [DllImport(Lib)] internal static extern int cuGetErrorString(int result, out IntPtr text);

    internal static void Check(int result, string operation)
    {
        if (result == CudaSuccess)
        {
            return;
        }

        string message = "unknown error";
        try
        {
            if (cuGetErrorString(result, out IntPtr text) == CudaSuccess && text != IntPtr.Zero)
            {
                message = Marshal.PtrToStringAnsi(text) ?? message;
            }
        }
        catch (DllNotFoundException)
        {
            // The driver itself is missing; keep the generic message.
        }

        throw new CudaException(result, operation, message);
    }
}
```

`src/XrplVanity.Gpu/CudaModule.cs`:

```csharp
using System;

namespace XrplVanity.Gpu;

public sealed class CudaFunction
{
    internal CudaFunction(IntPtr handle)
    {
        Handle = handle;
    }

    internal IntPtr Handle { get; }
}

public sealed class CudaModule
{
    private readonly IntPtr _handle;

    internal CudaModule(IntPtr handle)
    {
        _handle = handle;
    }

    public CudaFunction GetFunction(string name)
    {
        CudaDriver.Check(CudaDriver.cuModuleGetFunction(out IntPtr function, _handle, name), $"cuModuleGetFunction({name})");
        return new CudaFunction(function);
    }
}
```

`src/XrplVanity.Gpu/DeviceBuffer.cs`:

```csharp
using System;

namespace XrplVanity.Gpu;

public sealed class DeviceBuffer : IDisposable
{
    private bool _disposed;

    internal DeviceBuffer(ulong pointer, long size)
    {
        Pointer = pointer;
        Size = size;
    }

    public ulong Pointer { get; }
    public long Size { get; }

    public unsafe void CopyFrom(ReadOnlySpan<byte> source)
    {
        EnsureFits(source.Length);
        fixed (byte* p = source)
        {
            CudaDriver.Check(CudaDriver.cuMemcpyHtoD(Pointer, (IntPtr)p, (nuint)source.Length), "cuMemcpyHtoD");
        }
    }

    public unsafe void CopyTo(Span<byte> destination)
    {
        EnsureFits(destination.Length);
        fixed (byte* p = destination)
        {
            CudaDriver.Check(CudaDriver.cuMemcpyDtoH((IntPtr)p, Pointer, (nuint)destination.Length), "cuMemcpyDtoH");
        }
    }

    public void Clear()
    {
        CudaDriver.Check(CudaDriver.cuMemsetD8(Pointer, 0, (nuint)Size), "cuMemsetD8");
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
        _ = CudaDriver.cuMemFree(Pointer);
    }
}
```

`src/XrplVanity.Gpu/CudaContext.cs`:

```csharp
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace XrplVanity.Gpu;

/// <summary>One CUDA context on device 0. Not thread-safe; one per thread.</summary>
public sealed class CudaContext : IDisposable
{
    private readonly IntPtr _handle;
    private bool _disposed;

    private CudaContext(IntPtr handle, string deviceName, int major, int minor)
    {
        _handle = handle;
        DeviceName = deviceName;
        ComputeCapabilityMajor = major;
        ComputeCapabilityMinor = minor;
    }

    public string DeviceName { get; }
    public int ComputeCapabilityMajor { get; }
    public int ComputeCapabilityMinor { get; }

    /// <summary>Creates a context on device 0; returns false with a reason instead of throwing.</summary>
    public static bool TryCreate(out CudaContext? context, out string reason)
    {
        context = null;
        try
        {
            int result = CudaDriver.cuInit(0);
            if (result != CudaDriver.CudaSuccess)
            {
                reason = $"cuInit failed with CUDA error {result}";
                return false;
            }

            CudaDriver.Check(CudaDriver.cuDeviceGetCount(out int count), "cuDeviceGetCount");
            if (count == 0)
            {
                reason = "no CUDA devices";
                return false;
            }

            CudaDriver.Check(CudaDriver.cuDeviceGet(out int device, 0), "cuDeviceGet");
            byte[] nameBytes = new byte[256];
            CudaDriver.Check(CudaDriver.cuDeviceGetName(nameBytes, nameBytes.Length, device), "cuDeviceGetName");
            string name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
            CudaDriver.Check(CudaDriver.cuDeviceGetAttribute(out int major, CudaDriver.AttributeComputeCapabilityMajor, device), "cuDeviceGetAttribute(major)");
            CudaDriver.Check(CudaDriver.cuDeviceGetAttribute(out int minor, CudaDriver.AttributeComputeCapabilityMinor, device), "cuDeviceGetAttribute(minor)");
            CudaDriver.Check(CudaDriver.cuCtxCreate(out IntPtr handle, 0, device), "cuCtxCreate");

            context = new CudaContext(handle, name, major, minor);
            reason = string.Empty;
            return true;
        }
        catch (DllNotFoundException)
        {
            reason = "CUDA driver library not found (nvcuda.dll / libcuda.so.1)";
            return false;
        }
        catch (CudaException ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    public CudaModule LoadModule(string ptx)
    {
        byte[] image = Encoding.ASCII.GetBytes(ptx + "\0");
        CudaDriver.Check(CudaDriver.cuModuleLoadDataEx(out IntPtr module, image, 0, IntPtr.Zero, IntPtr.Zero), "cuModuleLoadDataEx");
        return new CudaModule(module);
    }

    public DeviceBuffer Alloc(long bytes)
    {
        CudaDriver.Check(CudaDriver.cuMemAlloc(out ulong pointer, (nuint)bytes), "cuMemAlloc");
        return new DeviceBuffer(pointer, bytes);
    }

    /// <summary>
    /// Launches a 1-D grid. Each argument must match the kernel parameter type exactly:
    /// a <see cref="DeviceBuffer"/> (or <c>ulong</c>) for a pointer, <c>int</c>/<c>uint</c>/
    /// <c>long</c>/<c>ulong</c>/<c>byte</c> for scalars.
    /// </summary>
    public void Launch(CudaFunction function, int gridX, int blockX, params object[] args)
    {
        GCHandle[] handles = new GCHandle[args.Length];
        IntPtr[] pointers = new IntPtr[args.Length];
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                object value = args[i] switch
                {
                    DeviceBuffer buffer => buffer.Pointer,
                    ulong or long or int or uint or byte => args[i],
                    _ => throw new ArgumentException($"Unsupported kernel argument type {args[i]?.GetType().Name ?? "null"} at index {i}."),
                };
                handles[i] = GCHandle.Alloc(value, GCHandleType.Pinned);
                pointers[i] = handles[i].AddrOfPinnedObject();
            }

            CudaDriver.Check(
                CudaDriver.cuLaunchKernel(function.Handle, (uint)gridX, 1, 1, (uint)blockX, 1, 1, 0, IntPtr.Zero, pointers, IntPtr.Zero),
                "cuLaunchKernel");
        }
        finally
        {
            foreach (GCHandle handle in handles)
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }
        }
    }

    public void Synchronize()
    {
        CudaDriver.Check(CudaDriver.cuCtxSynchronize(), "cuCtxSynchronize");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = CudaDriver.cuCtxDestroy(_handle);
    }
}
```

`src/XrplVanity.Gpu/GpuAvailability.cs`:

```csharp
namespace XrplVanity.Gpu;

public static class GpuAvailability
{
    /// <summary>Probes for a usable CUDA device without throwing.</summary>
    public static (bool Available, string Info) Detect()
    {
        if (!CudaContext.TryCreate(out CudaContext? context, out string reason))
        {
            return (false, reason);
        }

        using (context)
        {
            return (true, $"{context!.DeviceName} (sm_{context.ComputeCapabilityMajor}{context.ComputeCapabilityMinor})");
        }
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuInfrastructureTests"`
Expected: PASS (3 tests) on the reference machine (RTX 5070 Ti); on a machine without CUDA all three report Skipped, never Failed. Build must be warning-free.

- [ ] **Step 6: Commit**

```bash
git add XrplVanity.sln src/XrplVanity.Gpu tests/XrplVanity.Tests
git commit -m "feat(gpu): CUDA driver API layer and device detection"
```

---

### Task 2: Kernel source, PTX build script, embedded PTX, first launch (M0)

**Files:**
- Create: `gpu/kernels/xrpl_vanity.cu`, `gpu/build-ptx.py`, `gpu/kernels/xrpl_vanity.ptx` (generated), `src/XrplVanity.Gpu/GpuKernels.cs`
- Modify: `src/XrplVanity.Gpu/XrplVanity.Gpu.csproj` (embed the PTX)
- Test: `tests/XrplVanity.Tests/Gpu/GpuInfrastructureTests.cs` (add one test)

**Interfaces:**
- Consumes: `CudaContext`, `DeviceBuffer`, `GpuTestHelper.RequireContext`.
- Produces: `static class XrplVanity.Gpu.GpuKernels` with `static string ReadPtx()`; kernel entry point `k_add_arrays(const unsigned int* a, const unsigned int* b, unsigned int* out, int n)`; the `.cu` skeleton (typedefs) every later task appends to.

- [ ] **Step 1: Write the failing test**

Append to `tests/XrplVanity.Tests/Gpu/GpuInfrastructureTests.cs` inside the class:

```csharp
    [Fact]
    public void Embedded_Ptx_Loads_And_A_Kernel_Runs()
    {
        using CudaContext context = GpuTestHelper.RequireContext();
        CudaModule module = context.LoadModule(GpuKernels.ReadPtx());
        CudaFunction add = module.GetFunction("k_add_arrays");

        const int n = 1000;
        uint[] a = new uint[n];
        uint[] b = new uint[n];
        for (int i = 0; i < n; i++)
        {
            a[i] = (uint)i;
            b[i] = (uint)(i * 3);
        }

        using DeviceBuffer da = context.Alloc(n * 4);
        using DeviceBuffer db = context.Alloc(n * 4);
        using DeviceBuffer dout = context.Alloc(n * 4);
        da.CopyFrom(GpuTestHelper.AsBytes(a));
        db.CopyFrom(GpuTestHelper.AsBytes(b));

        context.Launch(add, (n + 255) / 256, 256, da, db, dout, n);
        context.Synchronize();

        byte[] raw = new byte[n * 4];
        dout.CopyTo(raw);
        uint[] result = GpuTestHelper.AsUInts(raw);
        for (int i = 0; i < n; i++)
        {
            Assert.Equal(a[i] + b[i], result[i]);
        }
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuInfrastructureTests"`
Expected: build error — `GpuKernels` does not exist.

- [ ] **Step 3: Write the kernel skeleton, the build script, and the resource loader**

`gpu/kernels/xrpl_vanity.cu` (device-only; no host code; NVRTC-compilable):

```cpp
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
```

`gpu/build-ptx.py`:

```python
#!/usr/bin/env python3
"""Compile gpu/kernels/xrpl_vanity.cu to PTX with NVRTC. No host C++ compiler needed.

NVRTC comes from the `nvidia-cuda-nvrtc` pip wheel (pip install --user nvidia-cuda-nvrtc);
set NVRTC_DIR to override the lookup. Target: compute_75 (Turing and newer; the driver
JIT-compiles the PTX for the actual GPU).
"""
import ctypes as C
import os
import site
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CU = os.path.join(ROOT, "gpu", "kernels", "xrpl_vanity.cu")
PTX = os.path.join(ROOT, "gpu", "kernels", "xrpl_vanity.ptx")
ARCH = b"compute_75"


def find_nvrtc_dir() -> str:
    env = os.environ.get("NVRTC_DIR")
    if env:
        return env
    candidates = [site.getusersitepackages()] + site.getsitepackages()
    for base in candidates:
        for sub in (("nvidia", "cu13", "bin", "x86_64"), ("nvidia", "cu13", "lib")):
            path = os.path.join(base, *sub)
            if os.path.isdir(path):
                return path
    sys.exit("NVRTC not found: run `pip install --user nvidia-cuda-nvrtc` or set NVRTC_DIR")


def load_nvrtc(directory: str) -> C.CDLL:
    if os.name == "nt":
        if hasattr(os, "add_dll_directory"):
            os.add_dll_directory(directory)
        os.environ["PATH"] = directory + os.pathsep + os.environ.get("PATH", "")
        names = [f for f in os.listdir(directory) if f.startswith("nvrtc64_") and f.endswith(".dll") and "builtins" not in f]
    else:
        names = [f for f in os.listdir(directory) if f.startswith("libnvrtc.so") and "builtins" not in f]
    if not names:
        sys.exit(f"no NVRTC library in {directory}")
    return C.CDLL(os.path.join(directory, sorted(names)[0]))


def main() -> None:
    nvrtc = load_nvrtc(find_nvrtc_dir())
    with open(CU, "rb") as f:
        source = f.read()

    program = C.c_void_p()
    if nvrtc.nvrtcCreateProgram(C.byref(program), source, b"xrpl_vanity.cu", 0, None, None) != 0:
        sys.exit("nvrtcCreateProgram failed")

    options = [b"--gpu-architecture=" + ARCH]
    array = (C.c_char_p * len(options))(*options)
    rc = nvrtc.nvrtcCompileProgram(program, len(options), array)

    log_size = C.c_size_t()
    nvrtc.nvrtcGetProgramLogSize(program, C.byref(log_size))
    if log_size.value > 1:
        log = C.create_string_buffer(log_size.value)
        nvrtc.nvrtcGetProgramLog(program, log)
        sys.stderr.write(log.value.decode(errors="replace") + "\n")
    if rc != 0:
        sys.exit(f"NVRTC compile failed (rc={rc})")

    ptx_size = C.c_size_t()
    nvrtc.nvrtcGetPTXSize(program, C.byref(ptx_size))
    ptx = C.create_string_buffer(ptx_size.value)
    nvrtc.nvrtcGetPTX(program, ptx)
    with open(PTX, "wb") as f:
        f.write(ptx.value)
    print(f"wrote {PTX} ({len(ptx.value)} bytes, {ARCH.decode()})")


if __name__ == "__main__":
    main()
```

Generate the PTX:

```bash
cd E:/GIT/XRPL/xrpl-vanity
python gpu/build-ptx.py
```

Expected: `wrote .../gpu/kernels/xrpl_vanity.ptx (N bytes, compute_75)`.

`src/XrplVanity.Gpu/GpuKernels.cs`:

```csharp
using System.IO;
using System.Reflection;

namespace XrplVanity.Gpu;

public static class GpuKernels
{
    private const string ResourceName = "xrpl_vanity.ptx";

    /// <summary>The PTX generated from gpu/kernels/xrpl_vanity.cu, embedded at build time.</summary>
    public static string ReadPtx()
    {
        Assembly assembly = typeof(GpuKernels).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new FileNotFoundException($"Embedded resource '{ResourceName}' is missing; run gpu/build-ptx.py and rebuild.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
```

Add to `src/XrplVanity.Gpu/XrplVanity.Gpu.csproj` before `</Project>`:

```xml
  <ItemGroup>
    <EmbeddedResource Include="..\..\gpu\kernels\xrpl_vanity.ptx" LogicalName="xrpl_vanity.ptx" />
  </ItemGroup>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuInfrastructureTests"`
Expected: PASS (4 tests). Also run the whole suite once: everything green, warning-free.

- [ ] **Step 5: Commit**

```bash
git add gpu/kernels/xrpl_vanity.cu gpu/kernels/xrpl_vanity.ptx gpu/build-ptx.py src/XrplVanity.Gpu tests/XrplVanity.Tests
git commit -m "feat(gpu): kernel source, NVRTC PTX build script, embedded PTX, first launch"
```

---

### Task 3: Field arithmetic mod 2^255-19 on the device (M1)

**Files:**
- Modify: `gpu/kernels/xrpl_vanity.cu` (append the field section), regenerate `gpu/kernels/xrpl_vanity.ptx`
- Test: `tests/XrplVanity.Tests/Gpu/GpuFieldTests.cs`

**Interfaces:**
- Consumes: `CudaContext`, `GpuKernels.ReadPtx`, `GpuTestHelper` limb helpers.
- Produces (device): `struct Fe { u32 v[8]; }` (little-endian limbs, canonical `< p` on output); `fe_add`, `fe_sub`, `fe_mul`, `fe_sqr`, `fe_inv`, `fe_canon`, `fe_from_bytes`, `fe_to_bytes`; test entry `k_fe_op(const u32* a, const u32* b, u32* out, int n, int op)` with `op` 0=add, 1=sub, 2=mul, 3=sqr, 4=inv, 5=canon (reduce an arbitrary `< 2^256` input).

- [ ] **Step 1: Write the failing test**

`tests/XrplVanity.Tests/Gpu/GpuFieldTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using XrplVanity.Gpu;
using Xunit;

namespace XrplVanity.Tests.Gpu;

public class GpuFieldTests
{
    private const int OpAdd = 0, OpSub = 1, OpMul = 2, OpSqr = 3, OpInv = 4, OpCanon = 5;

    private static readonly BigInteger P = GpuTestHelper.P;

    private static uint[] RunOp(int op, BigInteger[] a, BigInteger[] b)
    {
        using CudaContext context = GpuTestHelper.RequireContext();
        CudaModule module = context.LoadModule(GpuKernels.ReadPtx());
        CudaFunction fn = module.GetFunction("k_fe_op");

        int n = a.Length;
        uint[] la = new uint[n * 8];
        uint[] lb = new uint[n * 8];
        for (int i = 0; i < n; i++)
        {
            GpuTestHelper.ToLimbs(a[i]).CopyTo(la, i * 8);
            GpuTestHelper.ToLimbs(b[i]).CopyTo(lb, i * 8);
        }

        using DeviceBuffer da = context.Alloc(n * 32);
        using DeviceBuffer db = context.Alloc(n * 32);
        using DeviceBuffer dout = context.Alloc(n * 32);
        da.CopyFrom(GpuTestHelper.AsBytes(la));
        db.CopyFrom(GpuTestHelper.AsBytes(lb));
        context.Launch(fn, (n + 255) / 256, 256, da, db, dout, n, op);
        context.Synchronize();

        byte[] raw = new byte[n * 32];
        dout.CopyTo(raw);
        return GpuTestHelper.AsUInts(raw);
    }

    private static BigInteger[] Inputs()
    {
        // Deterministic mix of edge values and random field elements.
        List<BigInteger> values = new()
        {
            BigInteger.Zero, BigInteger.One, 2, P - 1, P - 2, (P - 1) / 2, P / 2 + 1,
            BigInteger.Pow(2, 255) - 20, BigInteger.Pow(2, 254), BigInteger.Pow(2, 32) - 1,
            (BigInteger.One << 255) - 19 - 1,
        };
        Random rng = new(20260903);
        byte[] buf = new byte[32];
        for (int i = 0; i < 500; i++)
        {
            rng.NextBytes(buf);
            BigInteger v = new(buf, isUnsigned: true, isBigEndian: false) % P;
            values.Add(v);
        }

        return values.ToArray();
    }

    private static void AssertEach(uint[] limbs, BigInteger[] expected)
    {
        for (int i = 0; i < expected.Length; i++)
        {
            BigInteger got = GpuTestHelper.FromLimbs(limbs.AsSpan(i * 8, 8));
            Assert.True(expected[i] == got, $"index {i}: expected {expected[i]} got {got}");
            Assert.True(got < P, $"index {i}: result {got} is not canonical");
        }
    }

    [Fact]
    public void Add_Sub_Mul_Sqr_Match_BigInteger()
    {
        BigInteger[] a = Inputs();
        BigInteger[] b = new BigInteger[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            b[i] = a[(i * 7 + 3) % a.Length];
        }

        AssertEach(RunOp(OpAdd, a, b), Map(a, b, (x, y) => (x + y) % P));
        AssertEach(RunOp(OpSub, a, b), Map(a, b, (x, y) => ((x - y) % P + P) % P));
        AssertEach(RunOp(OpMul, a, b), Map(a, b, (x, y) => (x * y) % P));
        AssertEach(RunOp(OpSqr, a, b), Map(a, b, (x, _) => (x * x) % P));
    }

    [Fact]
    public void Inverse_Times_Value_Is_One()
    {
        BigInteger[] a = Array.FindAll(Inputs(), v => v != 0);
        uint[] inv = RunOp(OpInv, a, a);
        for (int i = 0; i < a.Length; i++)
        {
            BigInteger got = GpuTestHelper.FromLimbs(inv.AsSpan(i * 8, 8));
            Assert.True((a[i] * got) % P == 1, $"index {i}: {a[i]} * {got} != 1 mod p");
            Assert.True(got == BigInteger.ModPow(a[i], P - 2, P), $"index {i}: inverse differs from a^(p-2)");
        }
    }

    [Fact]
    public void Canon_Reduces_Any_256_Bit_Value()
    {
        // Values >= p (not canonical) up to 2^256-1 must reduce to value mod p.
        BigInteger[] a =
        {
            P, P + 1, P + 37, 2 * P, 2 * P + 37, BigInteger.Pow(2, 256) - 1, BigInteger.Pow(2, 255),
        };
        AssertEach(RunOp(OpCanon, a, a), Map(a, a, (x, _) => x % P));
    }

    private static BigInteger[] Map(BigInteger[] a, BigInteger[] b, Func<BigInteger, BigInteger, BigInteger> f)
    {
        BigInteger[] r = new BigInteger[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            r[i] = f(a[i], b[i]);
        }

        return r;
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuFieldTests"`
Expected: FAIL — `CudaException` from `cuModuleGetFunction(k_fe_op)` (named symbol not found).

- [ ] **Step 3: Append the field section to the kernel and regenerate the PTX**

Append to `gpu/kernels/xrpl_vanity.cu`:

```cpp
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

// Test entry: element-wise field op over n elements. op: 0 add, 1 sub, 2 mul, 3 sqr, 4 inv, 5 canon.
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
        default: fe_copy(r, x); fe_canon(r); break;
    }
    for (int k = 0; k < 8; k++) out[i * 8 + k] = r.v[k];
}
```

Regenerate: `python gpu/build-ptx.py`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuFieldTests"`
Expected: PASS (3 tests): 511 inputs for add/sub/mul/sqr, inverses verified both by `a * inv == 1` and by equality with `ModPow(a, p-2)`, and canonicalisation of non-reduced inputs.

- [ ] **Step 5: Commit**

```bash
git add gpu/kernels/xrpl_vanity.cu gpu/kernels/xrpl_vanity.ptx tests/XrplVanity.Tests
git commit -m "feat(gpu): field arithmetic mod 2^255-19 verified against BigInteger"
```

---

### Task 4: Point arithmetic, naive fixed-base scalar multiplication, compression (M2)

**Files:**
- Modify: `gpu/kernels/xrpl_vanity.cu` (append the point section), regenerate PTX
- Test: `tests/XrplVanity.Tests/Gpu/GpuPointTests.cs`

**Interfaces:**
- Consumes: the field section (`Fe`, `fe_*`), `CudaContext`, `GpuTestHelper`.
- Produces (device): `struct Pt { Fe X, Y, Z, T; }`; `pt_identity`, `pt_base`, `pt_add`, `pt_dbl`, `scalarmult_base(Pt& r, const u8* scalar32)` (scalar little-endian, already clamped), `pt_compress(u8* out32, const Pt& p)`; test entry `k_scalarmult_base(const u8* scalars, u8* pubs, int n)` (n x 32 in, n x 32 out).

The host clamps the scalar exactly as RFC 8032 does — `a = SHA-512(seed)[0..32]; a[0] &= 248; a[31] &= 127; a[31] |= 64` — and hands the device the raw scalar, so this task tests the curve arithmetic in isolation from the hashing that Task 5 adds.

- [ ] **Step 1: Write the failing test**

`tests/XrplVanity.Tests/Gpu/GpuPointTests.cs`:

```csharp
using System;
using System.Security.Cryptography;
using XrplVanity.Gpu;
using Xunit;

namespace XrplVanity.Tests.Gpu;

public class GpuPointTests
{
    /// <summary>RFC 8032 clamping of SHA-512(seed) into the private scalar.</summary>
    private static byte[] ClampedScalar(byte[] seed32)
    {
        byte[] h = SHA512.HashData(seed32);
        byte[] a = new byte[32];
        Array.Copy(h, a, 32);
        a[0] &= 248;
        a[31] &= 127;
        a[31] |= 64;
        return a;
    }

    private static byte[][] RunScalarMult(byte[][] scalars)
    {
        using CudaContext context = GpuTestHelper.RequireContext();
        CudaModule module = context.LoadModule(GpuKernels.ReadPtx());
        CudaFunction fn = module.GetFunction("k_scalarmult_base");

        int n = scalars.Length;
        byte[] input = new byte[n * 32];
        for (int i = 0; i < n; i++)
        {
            scalars[i].CopyTo(input, i * 32);
        }

        using DeviceBuffer din = context.Alloc(n * 32);
        using DeviceBuffer dout = context.Alloc(n * 32);
        din.CopyFrom(input);
        context.Launch(fn, (n + 127) / 128, 128, din, dout, n);
        context.Synchronize();

        byte[] output = new byte[n * 32];
        dout.CopyTo(output);
        byte[][] pubs = new byte[n][];
        for (int i = 0; i < n; i++)
        {
            pubs[i] = output.AsSpan(i * 32, 32).ToArray();
        }

        return pubs;
    }

    [Theory]
    [InlineData("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60", "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a")]
    [InlineData("4ccd089b28ff96da9db6c346ec114e0f5b8a319f35aba624da8cf6ed4fb8a6fb", "3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c")]
    [InlineData("c5aa8df43f9f837bedb7442f31dcb7b166d38535076f094b85ce3a2e0b4458f7", "fc51cd8e6218a1a38da47ed00230f0580816ed13ba3303ac5deb911548908025")]
    public void Reproduces_Rfc8032_Public_Keys(string seedHex, string publicHex)
    {
        byte[] scalar = ClampedScalar(Convert.FromHexString(seedHex));

        byte[][] pubs = RunScalarMult(new[] { scalar });

        Assert.Equal(publicHex, Convert.ToHexString(pubs[0]).ToLowerInvariant());
    }

    [Fact]
    public void Matches_BouncyCastle_For_Random_Seeds()
    {
        const int n = 256;
        Random rng = new(20260903);
        byte[][] scalars = new byte[n][];
        byte[][] expected = new byte[n][];
        for (int i = 0; i < n; i++)
        {
            byte[] seed = new byte[32];
            rng.NextBytes(seed);
            scalars[i] = ClampedScalar(seed);
            expected[i] = new byte[32];
            Org.BouncyCastle.Math.EC.Rfc8032.Ed25519.GeneratePublicKey(seed, expected[i]);
        }

        byte[][] pubs = RunScalarMult(scalars);

        for (int i = 0; i < n; i++)
        {
            Assert.True(expected[i].AsSpan().SequenceEqual(pubs[i]), $"seed index {i} public key mismatch");
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuPointTests"`
Expected: FAIL — `cuModuleGetFunction(k_scalarmult_base)` named symbol not found.

- [ ] **Step 3: Append the point section and regenerate the PTX**

Append to `gpu/kernels/xrpl_vanity.cu`:

```cpp
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
```

Regenerate: `python gpu/build-ptx.py`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuPointTests"`
Expected: PASS (4 tests: three RFC 8032 vectors + 256 random seeds against BouncyCastle). Then the whole suite: green, warning-free.

- [ ] **Step 5: Commit**

```bash
git add gpu/kernels/xrpl_vanity.cu gpu/kernels/xrpl_vanity.ptx tests/XrplVanity.Tests
git commit -m "feat(gpu): Ed25519 point arithmetic and scalar multiplication verified against RFC 8032"
```

---

### Task 5: SHA-512, SHA-256, RIPEMD-160 and base58 on the device (M3)

**Files:**
- Modify: `gpu/kernels/xrpl_vanity.cu` (append the hash section), regenerate PTX
- Test: `tests/XrplVanity.Tests/Gpu/GpuHashTests.cs`

**Interfaces:**
- Consumes: `CudaContext`, `GpuTestHelper`.
- Produces (device): `sha512(const u8* msg, int len, u8* out64)` (len ≤ 239), `sha256(const u8* msg, int len, u8* out32)` (len ≤ 183), `ripemd160(const u8* msg, int len, u8* out20)` (len ≤ 119), `int base58_encode(const u8* in, int len, char* out)` (len ≤ 32; returns char count); test entries `k_sha512(const u8* in, u8* out, int n, int inLen)`, `k_sha256(...)`, `k_ripemd160(...)` (each thread hashes `in[i*inLen .. +inLen]`), `k_base58(const u8* in, u8* out, int n)` (n x 25-byte payloads → n x 36 bytes: `[length][chars…][zero padding]`).

- [ ] **Step 1: Write the failing test**

`tests/XrplVanity.Tests/Gpu/GpuHashTests.cs`:

```csharp
using System;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Digests;
using XrplVanity.Core;
using XrplVanity.Gpu;
using Xunit;

namespace XrplVanity.Tests.Gpu;

public class GpuHashTests
{
    private static byte[] RunHash(string kernel, byte[][] inputs, int inLen, int outLen)
    {
        using CudaContext context = GpuTestHelper.RequireContext();
        CudaModule module = context.LoadModule(GpuKernels.ReadPtx());
        CudaFunction fn = module.GetFunction(kernel);

        int n = inputs.Length;
        byte[] flat = new byte[n * inLen];
        for (int i = 0; i < n; i++)
        {
            inputs[i].CopyTo(flat, i * inLen);
        }

        using DeviceBuffer din = context.Alloc(Math.Max(1, flat.Length));
        using DeviceBuffer dout = context.Alloc(n * outLen);
        if (flat.Length > 0)
        {
            din.CopyFrom(flat);
        }

        context.Launch(fn, (n + 255) / 256, 256, din, dout, n, inLen);
        context.Synchronize();

        byte[] output = new byte[n * outLen];
        dout.CopyTo(output);
        return output;
    }

    private static byte[][] RandomInputs(int count, int length, int seed)
    {
        Random rng = new(seed);
        byte[][] inputs = new byte[count][];
        for (int i = 0; i < count; i++)
        {
            inputs[i] = new byte[length];
            rng.NextBytes(inputs[i]);
        }

        return inputs;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(40)]
    [InlineData(111)]
    [InlineData(112)]
    [InlineData(200)]
    public void Sha512_Matches_DotNet(int length)
    {
        byte[][] inputs = RandomInputs(64, length, 1000 + length);
        byte[] output = RunHash("k_sha512", inputs, length, 64);
        for (int i = 0; i < inputs.Length; i++)
        {
            byte[] expected = SHA512.HashData(inputs[i]);
            Assert.True(expected.AsSpan().SequenceEqual(output.AsSpan(i * 64, 64)), $"len {length} index {i}");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(55)]
    [InlineData(56)]
    [InlineData(120)]
    public void Sha256_Matches_DotNet(int length)
    {
        byte[][] inputs = RandomInputs(64, length, 2000 + length);
        byte[] output = RunHash("k_sha256", inputs, length, 32);
        for (int i = 0; i < inputs.Length; i++)
        {
            byte[] expected = SHA256.HashData(inputs[i]);
            Assert.True(expected.AsSpan().SequenceEqual(output.AsSpan(i * 32, 32)), $"len {length} index {i}");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(32)]
    [InlineData(55)]
    [InlineData(56)]
    [InlineData(100)]
    public void Ripemd160_Matches_BouncyCastle(int length)
    {
        byte[][] inputs = RandomInputs(64, length, 3000 + length);
        byte[] output = RunHash("k_ripemd160", inputs, length, 20);
        RipeMD160Digest digest = new();
        for (int i = 0; i < inputs.Length; i++)
        {
            byte[] expected = new byte[20];
            digest.BlockUpdate(inputs[i], 0, inputs[i].Length);
            digest.DoFinal(expected, 0);
            Assert.True(expected.AsSpan().SequenceEqual(output.AsSpan(i * 20, 20)), $"len {length} index {i}");
        }
    }

    [Fact]
    public void Ripemd160_Known_Vectors()
    {
        byte[][] inputs = { Array.Empty<byte>(), Encoding.ASCII.GetBytes("abc") };
        byte[] empty = RunHash("k_ripemd160", new[] { inputs[0] }, 0, 20);
        byte[] abc = RunHash("k_ripemd160", new[] { inputs[1] }, 3, 20);
        Assert.Equal("9c1185a5c5e9fc54612808977ee8f548b2258d31", Convert.ToHexString(empty).ToLowerInvariant());
        Assert.Equal("8eb208f7e05d987a9b044a8e98c6b087f15a0bfc", Convert.ToHexString(abc).ToLowerInvariant());
    }

    [Fact]
    public void Base58_Matches_Core_Encoder()
    {
        const int n = 500;
        Random rng = new(4000);
        byte[][] payloads = new byte[n][];
        for (int i = 0; i < n; i++)
        {
            payloads[i] = new byte[25];
            rng.NextBytes(payloads[i]);
            payloads[i][0] = 0x00; // AccountID version byte, as in a real address
        }

        // Also force the leading-zero edge cases the encoder must reproduce.
        payloads[0].AsSpan().Clear();
        payloads[1].AsSpan().Clear();
        payloads[1][24] = 1;

        using CudaContext context = GpuTestHelper.RequireContext();
        CudaModule module = context.LoadModule(GpuKernels.ReadPtx());
        CudaFunction fn = module.GetFunction("k_base58");
        byte[] flat = new byte[n * 25];
        for (int i = 0; i < n; i++)
        {
            payloads[i].CopyTo(flat, i * 25);
        }

        using DeviceBuffer din = context.Alloc(flat.Length);
        using DeviceBuffer dout = context.Alloc(n * 36);
        din.CopyFrom(flat);
        context.Launch(fn, (n + 255) / 256, 256, din, dout, n);
        context.Synchronize();
        byte[] output = new byte[n * 36];
        dout.CopyTo(output);

        Span<char> buffer = stackalloc char[64];
        for (int i = 0; i < n; i++)
        {
            int len = XrplBase58.Encode(payloads[i], buffer);
            string expected = new(buffer[..len]);
            int gotLen = output[i * 36];
            string got = Encoding.ASCII.GetString(output, i * 36 + 1, gotLen);
            Assert.True(expected == got, $"index {i}: expected {expected} got {got}");
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuHashTests"`
Expected: FAIL — `cuModuleGetFunction(k_sha512)` named symbol not found.

- [ ] **Step 3: Append the hash section and regenerate the PTX**

Append to `gpu/kernels/xrpl_vanity.cu`:

```cpp
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

// Port of XrplVanity.Core.XrplBase58.Encode for len <= 32 bytes. Returns the char count (<= 44).
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

// Test entries: each thread hashes in[i*inLen .. +inLen].
extern "C" __global__ void k_sha512(const u8* in, u8* out, int n, int inLen)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n) return;
    sha512(in + i * inLen, inLen, out + i * 64);
}
extern "C" __global__ void k_sha256(const u8* in, u8* out, int n, int inLen)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n) return;
    sha256(in + i * inLen, inLen, out + i * 32);
}
extern "C" __global__ void k_ripemd160(const u8* in, u8* out, int n, int inLen)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n) return;
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
```

Regenerate: `python gpu/build-ptx.py`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuHashTests"`
Expected: PASS (22 tests: 7 SHA-512 lengths incl. the 111/112-byte padding boundary, 7 SHA-256 lengths incl. 55/56, 6 RIPEMD-160 lengths incl. 55/56, the two RIPEMD known vectors, base58 over 500 payloads incl. all-zero and leading-zero cases). Then the whole suite: green, warning-free.

- [ ] **Step 5: Commit**

```bash
git add gpu/kernels/xrpl_vanity.cu gpu/kernels/xrpl_vanity.ptx tests/XrplVanity.Tests
git commit -m "feat(gpu): SHA-512, SHA-256, RIPEMD-160 and base58 on the device, verified against .NET and BouncyCastle"
```

---

### Task 6: Full entropy → address pipeline and the 1,000,000-input cross-check (M4)

**Files:**
- Modify: `gpu/kernels/xrpl_vanity.cu` (append the pipeline section), regenerate PTX
- Test: `tests/XrplVanity.Tests/Gpu/GpuPipelineTests.cs`

**Interfaces:**
- Consumes: all previous device sections; `XrplVanity.Core.BouncyCastleDeriver`, `Seed.DecodeEntropy`; `CudaContext`, `GpuTestHelper`.
- Produces (device): `derive_address(const u8* entropy16, u8* out36)` (writes `[length][chars…][zero padding]`); kernels `k_derive_address(const u8* entropy, u8* out, int n)` (n x 16 → n x 36) and `k_prf_entropy(const u8* key32, u64 start, u8* out, int n)` (`out[i] = SHA-512(key32 || LE64(start + i))[0..16]`). These are the building blocks Plan B's search kernel composes.

- [ ] **Step 1: Write the failing test**

`tests/XrplVanity.Tests/Gpu/GpuPipelineTests.cs`:

```csharp
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using XrplVanity.Core;
using XrplVanity.Gpu;
using Xunit;

namespace XrplVanity.Tests.Gpu;

public class GpuPipelineTests
{
    private static byte[] RunDerive(byte[] entropies, int n)
    {
        using CudaContext context = GpuTestHelper.RequireContext();
        CudaModule module = context.LoadModule(GpuKernels.ReadPtx());
        CudaFunction fn = module.GetFunction("k_derive_address");

        using DeviceBuffer din = context.Alloc(entropies.Length);
        using DeviceBuffer dout = context.Alloc((long)n * 36);
        din.CopyFrom(entropies);
        context.Launch(fn, (n + 127) / 128, 128, din, dout, n);
        context.Synchronize();

        byte[] output = new byte[n * 36];
        dout.CopyTo(output);
        return output;
    }

    private static string AddressAt(byte[] output, int i)
    {
        int len = output[i * 36];
        return Encoding.ASCII.GetString(output, i * 36 + 1, len);
    }

    [Fact]
    public void Derives_The_Known_Vector()
    {
        byte[] entropy = Seed.DecodeEntropy("sEdTuRgwYiEcubPJfRJUArVyn7AXkVj");

        byte[] output = RunDerive(entropy, 1);

        Assert.Equal("rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK", AddressAt(output, 0));
    }

    [Fact]
    public void One_Million_Random_Entropies_Match_The_Cpu_Reference()
    {
        const int n = 1_000_000;
        byte[] entropies = new byte[n * 16];
        new Random(20260903).NextBytes(entropies);

        byte[] output = RunDerive(entropies, n);

        // CPU reference, one deriver per worker (BouncyCastleDeriver is not thread-safe).
        int mismatches = 0;
        string firstMismatch = string.Empty;
        object gate = new();
        Parallel.For(0, Environment.ProcessorCount, worker =>
        {
            BouncyCastleDeriver deriver = new();
            Span<char> buffer = stackalloc char[48];
            for (int i = worker; i < n; i += Environment.ProcessorCount)
            {
                int len = deriver.DeriveAddress(entropies.AsSpan(i * 16, 16), buffer);
                string expected = new(buffer[..len]);
                string got = AddressAt(output, i);
                if (expected != got)
                {
                    lock (gate)
                    {
                        mismatches++;
                        if (firstMismatch.Length == 0)
                        {
                            firstMismatch = $"index {i}: cpu {expected} gpu {got}";
                        }
                    }
                }
            }
        });

        Assert.True(mismatches == 0, $"{mismatches} of {n} addresses differ; first: {firstMismatch}");
    }

    [Fact]
    public void Prf_Entropy_Matches_Sha512_Of_Key_And_Counter()
    {
        byte[] key = new byte[32];
        new Random(777).NextBytes(key);
        const ulong start = 1_000_000_007UL;
        const int n = 1000;

        using CudaContext context = GpuTestHelper.RequireContext();
        CudaModule module = context.LoadModule(GpuKernels.ReadPtx());
        CudaFunction fn = module.GetFunction("k_prf_entropy");
        using DeviceBuffer dkey = context.Alloc(32);
        using DeviceBuffer dout = context.Alloc(n * 16);
        dkey.CopyFrom(key);
        context.Launch(fn, (n + 255) / 256, 256, dkey, start, dout, n);
        context.Synchronize();
        byte[] output = new byte[n * 16];
        dout.CopyTo(output);

        byte[] message = new byte[40];
        key.CopyTo(message, 0);
        for (int i = 0; i < n; i++)
        {
            BitConverter.TryWriteBytes(message.AsSpan(32, 8), start + (ulong)i); // little-endian on all supported platforms
            byte[] expected = SHA512.HashData(message);
            Assert.True(expected.AsSpan(0, 16).SequenceEqual(output.AsSpan(i * 16, 16)), $"counter {start + (ulong)i}");
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuPipelineTests"`
Expected: FAIL — `cuModuleGetFunction(k_derive_address)` named symbol not found.

- [ ] **Step 3: Append the pipeline section and regenerate the PTX**

Append to `gpu/kernels/xrpl_vanity.cu`:

```cpp
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
```

Regenerate: `python gpu/build-ptx.py`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*GpuPipelineTests"`
Expected: PASS (3 tests). The million-input test takes a few seconds (GPU ~1 s, CPU reference parallelised across cores). Then the whole suite: green, warning-free. Record the wall time of the 1M test in the commit message body.

- [ ] **Step 5: Commit**

```bash
git add gpu/kernels/xrpl_vanity.cu gpu/kernels/xrpl_vanity.ptx tests/XrplVanity.Tests
git commit -m "feat(gpu): entropy-to-address pipeline verified against the CPU reference over 1,000,000 inputs"
```

---

## Notes for the executor

- Regenerate and commit the PTX with every kernel change; a stale PTX makes tests fail with "named symbol not found" for the newest kernel.
- Every device function that returns a field element must leave it canonical (`< p`); the field tests assert this and later stages depend on it (equality, encoding).
- If the 1,000,000 cross-check reports mismatches, the bug is in the device pipeline or one of its stages — never in `BouncyCastleDeriver`, which is itself pinned by the known vector and the SDK cross-check. Bisect with the M1–M3 test kernels on the failing entropy.
- Do not add search, matching, hit buffers or a `--gpu` flag here; those are Plan B (M5–M6).
- Do not push, publish, or open a pull request.
