using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using XrplVanity.Gpu;
using Xunit;

namespace XrplVanity.Tests.Gpu;

internal static class GpuTestHelper
{
    /// <summary>
    /// Returns a live CUDA context, or skips the calling test when CUDA is unavailable or the
    /// device is older than the PTX's compute_75 (Turing) target.
    /// </summary>
    /// <remarks>
    /// Kept for tests that probe <see cref="CudaContext"/>-specific surface (device name, compute
    /// capability) with no equivalent on <see cref="IGpuDevice"/>; <see cref="GpuSearchEngine"/>-based
    /// tests were migrated to <see cref="RequireDevice"/> in Task 5.
    /// </remarks>
    public static CudaContext RequireContext()
    {
        if (!CudaContext.TryCreate(out CudaContext? context, out string reason))
        {
            Assert.Skip($"CUDA not available: {reason}");
        }

        int capability = context!.ComputeCapabilityMajor * 10 + context.ComputeCapabilityMinor;
        if (capability < 75)
        {
            string name = context.DeviceName;
            int major = context.ComputeCapabilityMajor;
            int minor = context.ComputeCapabilityMinor;
            context.Dispose();
            Assert.Skip($"CUDA device {name} is compute capability {major}.{minor}; the PTX targets compute_75 (Turing) or newer");
        }

        return context!;
    }

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
        Span<uint> limbSpan = limbs;
        MemoryMarshal.AsBytes(limbSpan).Clear();
        bytes.AsSpan().CopyTo(MemoryMarshal.AsBytes(limbSpan));
        return limbs;
    }

    public static byte[] AsBytes(uint[] limbs) => MemoryMarshal.AsBytes<uint>(limbs).ToArray();

    public static uint[] AsUInts(ReadOnlySpan<byte> bytes) => MemoryMarshal.Cast<byte, uint>(bytes).ToArray();
}
