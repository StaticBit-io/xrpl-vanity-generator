using System;
using System.Numerics;
using System.Security.Cryptography;
using XrplVanity.Gpu;
using Xunit;

namespace XrplVanity.Tests.Gpu;

public class GpuWindowedTests
{
    private const int TableBytes = 256 * 96;

    private static byte[] Clamp(byte[] seed32)
    {
        byte[] h = SHA512.HashData(seed32);
        byte[] a = new byte[32];
        Array.Copy(h, a, 32);
        a[0] &= 248;
        a[31] &= 127;
        a[31] |= 64;
        return a;
    }

    private static DeviceBuffer BuildTable(CudaContext context, CudaModule module)
    {
        DeviceBuffer table = context.Alloc(TableBytes);
        context.Launch(module.GetFunction("k_build_base_table"), 2, 128, table);
        context.Synchronize();
        return table;
    }

    private static byte[] Run(string kernel, byte[][] scalars)
    {
        using CudaContext context = GpuTestHelper.RequireContext();
        CudaModule module = context.LoadModule(GpuKernels.ReadPtx());
        using DeviceBuffer ownTable = BuildTable(context, module);

        int n = scalars.Length;
        byte[] input = new byte[n * 32];
        for (int i = 0; i < n; i++)
        {
            scalars[i].CopyTo(input, i * 32);
        }

        using DeviceBuffer din = context.Alloc(n * 32);
        using DeviceBuffer dout = context.Alloc(n * 32);
        din.CopyFrom(input);
        if (kernel == "k_scalarmult_base_windowed")
        {
            context.Launch(module.GetFunction(kernel), (n + 127) / 128, 128, din, dout, n, ownTable);
        }
        else
        {
            context.Launch(module.GetFunction(kernel), (n + 127) / 128, 128, din, dout, n);
        }

        context.Synchronize();
        byte[] output = new byte[n * 32];
        dout.CopyTo(output);
        return output;
    }

    [Fact]
    public void Table_Entry_Zero_Is_The_Base_Point()
    {
        using CudaContext context = GpuTestHelper.RequireContext();
        CudaModule module = context.LoadModule(GpuKernels.ReadPtx());
        using DeviceBuffer table = BuildTable(context, module);
        byte[] raw = new byte[96];
        table.CopyTo(raw);

        BigInteger p = GpuTestHelper.P;
        BigInteger bx = BigInteger.Parse("15112221349535400772501151409588531511454012693041857206046113283949847762202");
        BigInteger by = BigInteger.Parse("46316835694926478169428394003475163141307993866256225615783033603165251855960");
        BigInteger d = BigInteger.Parse("37095705934669439343138083508754565189542113879843219016388785533085940283555");

        BigInteger ypx = new(raw.AsSpan(0, 32), isUnsigned: true, isBigEndian: false);
        BigInteger ymx = new(raw.AsSpan(32, 32), isUnsigned: true, isBigEndian: false);
        BigInteger xy2d = new(raw.AsSpan(64, 32), isUnsigned: true, isBigEndian: false);

        Assert.Equal((by + bx) % p, ypx);
        Assert.Equal(((by - bx) % p + p) % p, ymx);
        Assert.Equal(2 * d % p * bx % p * by % p, xy2d);
    }

    [Theory]
    [InlineData("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60", "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a")]
    [InlineData("4ccd089b28ff96da9db6c346ec114e0f5b8a319f35aba624da8cf6ed4fb8a6fb", "3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c")]
    [InlineData("c5aa8df43f9f837bedb7442f31dcb7b166d38535076f094b85ce3a2e0b4458f7", "fc51cd8e6218a1a38da47ed00230f0580816ed13ba3303ac5deb911548908025")]
    public void Windowed_Reproduces_Rfc8032_Public_Keys(string seedHex, string publicHex)
    {
        byte[] pub = Run("k_scalarmult_base_windowed", new[] { Clamp(Convert.FromHexString(seedHex)) });

        Assert.Equal(publicHex, Convert.ToHexString(pub).ToLowerInvariant());
    }

    [Fact]
    public void Windowed_Equals_Naive_For_Random_Clamped_Scalars()
    {
        const int n = 1000;
        Random rng = new(20260903);
        byte[][] scalars = new byte[n][];
        for (int i = 0; i < n; i++)
        {
            byte[] seed = new byte[32];
            rng.NextBytes(seed);
            scalars[i] = Clamp(seed);
        }

        byte[] naive = Run("k_scalarmult_base", scalars);
        byte[] windowed = Run("k_scalarmult_base_windowed", scalars);

        Assert.True(naive.AsSpan().SequenceEqual(windowed), "windowed and naive scalar multiplication disagree");
    }
}
