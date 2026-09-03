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
        Span<uint> limbSpan = limbs;
        MemoryMarshal.AsBytes(limbSpan).Clear();
        bytes.AsSpan().CopyTo(MemoryMarshal.AsBytes(limbSpan));
        return limbs;
    }

    public static byte[] AsBytes(uint[] limbs) => MemoryMarshal.AsBytes<uint>(limbs).ToArray();

    public static uint[] AsUInts(ReadOnlySpan<byte> bytes) => MemoryMarshal.Cast<byte, uint>(bytes).ToArray();
}
