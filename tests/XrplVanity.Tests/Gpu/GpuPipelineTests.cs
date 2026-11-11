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
    // Launching all n candidates in one cuLaunchKernel makes one very long kernel for large
    // n, which a display GPU under the default Windows WDDM TDR (2 seconds) can kill with
    // CUDA_ERROR_LAUNCH_TIMEOUT. Process in batches instead, allocating fresh device buffers
    // per batch so each launch stays short.
    private const int BatchSize = 65_536;

    private static byte[] RunDerive(string backend, byte[] entropies, int n)
    {
        using IGpuDevice device = GpuTestHelper.RequireDevice(backend);
        IGpuKernel fn = device.GetKernel("k_derive_address");

        using IGpuBuffer table = device.Alloc(256 * 96);
        device.Launch(device.GetKernel("k_build_base_table"), 256, 128, table);
        device.Synchronize();

        byte[] output = new byte[n * 36];

        for (int batchStart = 0; batchStart < n; batchStart += BatchSize)
        {
            int batchLength = Math.Min(BatchSize, n - batchStart);
            using IGpuBuffer bin = device.Alloc(batchLength * 16);
            using IGpuBuffer bout = device.Alloc(batchLength * 36);
            bin.CopyFrom(entropies.AsSpan(batchStart * 16, batchLength * 16));
            device.Launch(fn, batchLength, 128, bin, bout, batchLength, table);
            device.Synchronize();
            bout.CopyTo(output.AsSpan(batchStart * 36, batchLength * 36));
        }

        return output;
    }

    private static string AddressAt(byte[] output, int i)
    {
        int len = output[i * 36];
        return Encoding.ASCII.GetString(output, i * 36 + 1, len);
    }

    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void Derives_The_Known_Vector(string backend)
    {
        byte[] entropy = Seed.DecodeEntropy("sEdTuRgwYiEcubPJfRJUArVyn7AXkVj");

        byte[] output = RunDerive(backend, entropy, 1);

        Assert.Equal("rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK", AddressAt(output, 0));
    }

    /// <summary>
    /// The sample CI runs: one batch, enough to catch a kernel that is wrong for a whole class of
    /// inputs on an independent OpenCL implementation, in seconds rather than minutes on pocl.
    /// </summary>
    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void Random_Entropies_Match_The_Cpu_Reference(string backend)
    {
        AssertMatchesCpuReference(backend, BatchSize);
    }

    /// <summary>A million keys, for the rare carry or reduction case a small sample can miss.</summary>
    [Theory]
    [Trait(GpuTestHelper.Category, GpuTestHelper.RealGpu)]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void One_Million_Random_Entropies_Match_The_Cpu_Reference(string backend)
    {
        AssertMatchesCpuReference(backend, 1_000_000);
    }

    private static void AssertMatchesCpuReference(string backend, int n)
    {
        byte[] entropies = new byte[n * 16];
        new Random(20260903).NextBytes(entropies);

        byte[] output = RunDerive(backend, entropies, n);

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

    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void Prf_Entropy_Matches_Sha512_Of_Key_And_Counter(string backend)
    {
        byte[] key = new byte[32];
        new Random(777).NextBytes(key);
        const ulong start = 1_000_000_007UL;
        const int n = 1000;

        using IGpuDevice device = GpuTestHelper.RequireDevice(backend);
        IGpuKernel fn = device.GetKernel("k_prf_entropy");
        using IGpuBuffer dkey = device.Alloc(32);
        using IGpuBuffer dout = device.Alloc(n * 16);
        dkey.CopyFrom(key);
        device.Launch(fn, n, 256, dkey, start, dout, n);
        device.Synchronize();
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
