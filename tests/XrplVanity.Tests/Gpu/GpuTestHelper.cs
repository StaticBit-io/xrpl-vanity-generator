using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using XrplVanity.Gpu;
using Xunit;

namespace XrplVanity.Tests.Gpu;

internal static class GpuTestHelper
{
    /// <summary>The trait name carrying <see cref="RealGpu"/>.</summary>
    public const string Category = "Category";

    /// <summary>
    /// Marks a test that measures time or volume only a real GPU makes meaningful. On a CPU OpenCL
    /// device such as pocl these runs take minutes and prove nothing a fast test does not, so CI
    /// excludes them with <c>-trait- Category=RealGpu</c> and they run on a developer machine.
    /// </summary>
    public const string RealGpu = "RealGpu";

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
            SkipOrFail("cuda", reason);
        }

        int capability = context!.ComputeCapabilityMajor * 10 + context.ComputeCapabilityMinor;
        if (capability < 75)
        {
            string name = context.DeviceName;
            int major = context.ComputeCapabilityMajor;
            int minor = context.ComputeCapabilityMinor;
            context.Dispose();
            SkipOrFail("cuda", $"CUDA device {name} is compute capability {major}.{minor}; the PTX targets compute_75 (Turing) or newer");
        }

        return context!;
    }

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
            SkipOrFail(backend, reason);
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
                        SkipOrFail("cuda", $"CUDA device {probe.DeviceName} is compute capability {probe.ComputeCapabilityMajor}.{probe.ComputeCapabilityMinor}; the PTX targets compute_75 (Turing) or newer");
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
