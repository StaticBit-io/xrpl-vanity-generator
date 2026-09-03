extern alias BcCrypto;

using System;
using System.Security.Cryptography;
using System.Text;
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
        BcCrypto::Org.BouncyCastle.Crypto.Digests.RipeMD160Digest digest = new();
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
