using System;
using System.Collections.Generic;
using XrplVanity.Gpu;
using XrplVanity.Gpu.OpenCl;
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
            Assert.Skip($"no usable GPU device: {info}");
        }
    }

    [Fact]
    public void Context_Exposes_Device_Name_And_Compute_Capability()
    {
        using CudaContext context = GpuTestHelper.RequireContext();

        Assert.False(string.IsNullOrWhiteSpace(context.DeviceName));
        Assert.True(context.ComputeCapabilityMajor >= 7); // Turing (7.5) or newer per the spec
    }

    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void Device_Buffer_Roundtrips_Bytes(string backend)
    {
        using IGpuDevice device = GpuTestHelper.RequireDevice(backend);
        byte[] data = new byte[4096];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i * 7);
        }

        using IGpuBuffer buffer = device.Alloc(data.Length);
        buffer.CopyFrom(data);
        byte[] back = new byte[data.Length];
        buffer.CopyTo(back);

        Assert.Equal(data, back);
    }

    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void Kernel_Program_Loads_And_A_Kernel_Runs(string backend)
    {
        using IGpuDevice device = GpuTestHelper.RequireDevice(backend);
        IGpuKernel add = device.GetKernel("k_add_arrays");

        const int n = 1000;
        uint[] a = new uint[n];
        uint[] b = new uint[n];
        for (int i = 0; i < n; i++)
        {
            a[i] = (uint)i;
            b[i] = (uint)(i * 3);
        }

        using IGpuBuffer da = device.Alloc(n * 4);
        using IGpuBuffer db = device.Alloc(n * 4);
        using IGpuBuffer dout = device.Alloc(n * 4);
        da.CopyFrom(GpuTestHelper.AsBytes(a));
        db.CopyFrom(GpuTestHelper.AsBytes(b));

        device.Launch(add, n, 256, da, db, dout, n);
        device.Synchronize();

        byte[] raw = new byte[n * 4];
        dout.CopyTo(raw);
        uint[] result = GpuTestHelper.AsUInts(raw);
        for (int i = 0; i < n; i++)
        {
            Assert.Equal(a[i] + b[i], result[i]);
        }
    }

    /// <summary>
    /// Covers the OpenCL constructor-failure cleanup path (Task 3 fix round 1): a failed
    /// <see cref="OpenClBackend.Open"/> must not leave the backend or the driver in a state where a
    /// subsequent, valid <c>Open(0)</c> call fails or the process crashes. This drives the failure
    /// through the public API (an out-of-range index, rejected before <c>OpenClDevice</c> is
    /// constructed). The real-build-failure counterpart, which does exercise the internal
    /// <c>OpenClDevice</c> constructor with a deliberately broken kernel source, lives in
    /// <see cref="GpuBackendsTests.OpenClDevice_Constructor_Reports_Build_Failure_And_Leaves_The_Runtime_Usable"/>.
    /// </summary>
    [Fact]
    public void OpenCl_Open_With_Out_Of_Range_Index_Throws_And_Device_Zero_Still_Opens_Afterwards()
    {
        OpenClBackend backend = new();
        IReadOnlyList<GpuDeviceInfo> devices = backend.EnumerateDevices();
        if (devices.Count == 0)
        {
            GpuTestHelper.SkipOrFail("opencl", "no devices");
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => backend.Open(devices.Count));

        using IGpuDevice device = backend.Open(0);
        Assert.False(string.IsNullOrWhiteSpace(device.Name));
    }
}
