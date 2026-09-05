using System.Collections.Generic;
using System.Linq;
using XrplVanity.Gpu;
using XrplVanity.Search;
using Xunit;

namespace XrplVanity.Tests.Search;

public class DeviceCatalogTests
{
    private sealed class FakeBackend : IGpuBackend
    {
        private readonly List<GpuDeviceInfo> _devices;
        public FakeBackend(string name, params GpuDeviceInfo[] devices) { Name = name; _devices = devices.ToList(); }
        public string Name { get; }
        public IReadOnlyList<GpuDeviceInfo> EnumerateDevices() => _devices;
        public IGpuDevice Open(int index) => throw new System.InvalidOperationException("not opened in this test");
    }

    [Fact]
    public void Lists_Every_Device_Of_Every_Backend_In_Order_With_Its_Cpu_Tag()
    {
        IGpuBackend[] backends =
        {
            new FakeBackend("cuda", new GpuDeviceInfo("cuda", 0, "RTX", "sm_120")),
            new FakeBackend("opencl", new GpuDeviceInfo("opencl", 0, "RTX", "OpenCL 3.0"), new GpuDeviceInfo("opencl", 1, "pocl", "cpu", IsCpuDevice: true)),
        };

        IReadOnlyList<DeviceCatalogEntry> entries = DeviceCatalog.Enumerate(backends);

        Assert.Equal(new[] { "cuda:0", "opencl:0", "opencl:1" }, entries.Select(e => e.Id));
        Assert.Equal("RTX", entries[0].Name);
        Assert.Equal("sm_120", entries[0].Details);
        Assert.True(entries[2].IsCpuDevice);
        Assert.False(entries[0].IsCpuDevice);
    }

    [Fact]
    public void A_Backend_That_Throws_On_Enumeration_Is_Skipped()
    {
        IGpuBackend[] backends = { new ThrowingBackend(), new FakeBackend("opencl", new GpuDeviceInfo("opencl", 0, "X", "")) };
        Assert.Single(DeviceCatalog.Enumerate(backends));
    }

    private sealed class ThrowingBackend : IGpuBackend
    {
        public string Name => "cuda";
        public IReadOnlyList<GpuDeviceInfo> EnumerateDevices() => throw new System.InvalidOperationException("driver missing");
        public IGpuDevice Open(int index) => throw new System.InvalidOperationException();
    }

    [Fact]
    public void The_Real_Catalog_Does_Not_Throw()
    {
        IReadOnlyList<DeviceCatalogEntry> entries = DeviceCatalog.Enumerate();
        Assert.All(entries, e => Assert.Matches("^(cuda|opencl):[0-9]+$", e.Id));
    }
}
