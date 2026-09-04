using System;
using System.Collections.Generic;
using XrplVanity.Gpu;
using XrplVanity.Gpu.Cuda;
using Xunit;

namespace XrplVanity.Tests.Gpu;

public sealed class CudaEnumerationTests
{
    [Fact]
    public void TryEnumerate_Lists_Every_Ordinal_With_Name_And_Capability()
    {
        if (!CudaContext.TryEnumerate(out IReadOnlyList<CudaDeviceInfo> devices, out string reason))
        {
            GpuTestHelper.SkipOrFail("cuda", reason);
        }

        Assert.NotEmpty(devices);
        for (int i = 0; i < devices.Count; i++)
        {
            Assert.Equal(i, devices[i].Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(devices[i].Name));
            Assert.True(devices[i].ComputeCapabilityMajor >= 1);
        }
    }

    [Fact]
    public void Backend_Enumeration_Matches_TryEnumerate_And_Opens_Each_Ordinal()
    {
        if (!CudaContext.TryEnumerate(out IReadOnlyList<CudaDeviceInfo> devices, out string reason))
        {
            GpuTestHelper.SkipOrFail("cuda", reason);
        }

        CudaBackend backend = new();
        IReadOnlyList<GpuDeviceInfo> listed = backend.EnumerateDevices();
        Assert.Equal(devices.Count, listed.Count);
        for (int i = 0; i < listed.Count; i++)
        {
            Assert.Equal("cuda", listed[i].Backend);
            Assert.Equal(i, listed[i].Index);
            Assert.Equal(devices[i].Name, listed[i].Name);
            Assert.Equal($"sm_{devices[i].ComputeCapabilityMajor}{devices[i].ComputeCapabilityMinor}", listed[i].Details);
            Assert.False(listed[i].IsCpuDevice);

            using IGpuDevice opened = backend.Open(i);
            Assert.Contains(devices[i].Name, opened.Name, StringComparison.Ordinal);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => backend.Open(listed.Count));
    }

    [Fact]
    public void TryCreate_With_An_Out_Of_Range_Ordinal_Reports_The_Range()
    {
        if (!CudaContext.TryEnumerate(out IReadOnlyList<CudaDeviceInfo> devices, out string reason))
        {
            GpuTestHelper.SkipOrFail("cuda", reason);
        }

        Assert.False(CudaContext.TryCreate(devices.Count, out CudaContext? context, out string why));
        Assert.Null(context);
        Assert.Contains("out of range", why, StringComparison.Ordinal);
    }
}
