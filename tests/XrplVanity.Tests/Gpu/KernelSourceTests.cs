using System.Globalization;
using System.Text.RegularExpressions;
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
    public void Kernel_Batch_Size_Matches_The_Host_Launch_Divisor()
    {
        Match match = Regex.Match(GpuKernels.ReadCudaSource(), @"^#define\s+SEARCH_BATCH\s+(\d+)\s*$", RegexOptions.Multiline);
        Assert.True(match.Success, "the kernel body no longer defines SEARCH_BATCH");

        int kernelBatch = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.True(kernelBatch == GpuSearchEngine.CandidateBatch,
            $"kernel SEARCH_BATCH {kernelBatch} != GpuSearchEngine.CandidateBatch {GpuSearchEngine.CandidateBatch}: the launch would skip or repeat candidates without failing.");

        Assert.True(GpuSearchEngine.SubLaunchSize % kernelBatch == 0,
            $"SubLaunchSize {GpuSearchEngine.SubLaunchSize} is not a multiple of the batch {kernelBatch}, so the last thread would run a ragged group.");
    }

    [Fact]
    public void Body_Hash_Ignores_Line_Ending_Style()
    {
        Assert.Equal(GpuKernels.ComputeBodyHash("a\r\nb\r\n"), GpuKernels.ComputeBodyHash("a\nb\n"));
    }
}
