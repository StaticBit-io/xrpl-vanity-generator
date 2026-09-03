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
    // CUDA_ERROR_LAUNCH_TIMEOUT. Process in batches instead, reusing the same device buffers
    // via pointer offsets so the wall-clock cost stays the same.
    private const int BatchSize = 65_536;

    private static byte[] RunDerive(byte[] entropies, int n)
    {
        using CudaContext context = GpuTestHelper.RequireContext();
        CudaModule module = context.LoadModule(GpuKernels.ReadPtx());
        CudaFunction fn = module.GetFunction("k_derive_address");

        using DeviceBuffer din = context.Alloc(entropies.Length);
        using DeviceBuffer dout = context.Alloc((long)n * 36);
        din.CopyFrom(entropies);

        for (int batchStart = 0; batchStart < n; batchStart += BatchSize)
        {
            int batchLength = Math.Min(BatchSize, n - batchStart);
            ulong inPointer = din.Pointer + (ulong)batchStart * 16;
            ulong outPointer = dout.Pointer + (ulong)batchStart * 36;
            context.Launch(fn, (batchLength + 127) / 128, 128, inPointer, outPointer, batchLength);
            context.Synchronize();
        }

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
