using XrplVanity.Gpu;
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
            Assert.Skip($"CUDA not available: {info}");
        }
    }

    [Fact]
    public void Context_Exposes_Device_Name_And_Compute_Capability()
    {
        using CudaContext context = GpuTestHelper.RequireContext();

        Assert.False(string.IsNullOrWhiteSpace(context.DeviceName));
        Assert.True(context.ComputeCapabilityMajor >= 7); // Turing (7.5) or newer per the spec
    }

    [Fact]
    public void Device_Buffer_Roundtrips_Bytes()
    {
        using CudaContext context = GpuTestHelper.RequireContext();
        byte[] data = new byte[4096];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i * 7);
        }

        using DeviceBuffer buffer = context.Alloc(data.Length);
        buffer.CopyFrom(data);
        byte[] back = new byte[data.Length];
        buffer.CopyTo(back);

        Assert.Equal(data, back);
    }
}
