extern alias BcCrypto;

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
            BcCrypto::Org.BouncyCastle.Math.EC.Rfc8032.Ed25519.GeneratePublicKey(seed, expected[i]);
        }

        byte[][] pubs = RunScalarMult(scalars);

        for (int i = 0; i < n; i++)
        {
            Assert.True(expected[i].AsSpan().SequenceEqual(pubs[i]), $"seed index {i} public key mismatch");
        }
    }
}
