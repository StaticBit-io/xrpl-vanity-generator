using XrplVanity.Gpu;
using Xunit;

namespace XrplVanity.Tests.Gpu;

public sealed class KernelSourceTests
{
    [Fact]
    public void Embedded_Ptx_Is_Stamped_With_The_Hash_Of_The_Embedded_Cuda_Source()
    {
        string? stamped = GpuKernels.ReadPtxBodyHash();
        Assert.NotNull(stamped);

        string actual = GpuKernels.ComputeBodyHash(GpuKernels.ReadCudaSource());
        Assert.True(stamped == actual,
            $"PTX stamp {stamped} != body hash {actual}: the kernel body or cuda_shim.h changed without `python gpu/build-ptx.py` — regenerate and commit the PTX with the body.");
    }

    [Fact]
    public void Body_Hash_Ignores_Line_Ending_Style()
    {
        Assert.Equal(GpuKernels.ComputeBodyHash("a\r\nb\r\n"), GpuKernels.ComputeBodyHash("a\nb\n"));
    }
}
