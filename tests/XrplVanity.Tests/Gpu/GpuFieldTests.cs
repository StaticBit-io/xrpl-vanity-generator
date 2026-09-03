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
            BigInteger v = new BigInteger(buf, isUnsigned: true, isBigEndian: false) % P;
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
