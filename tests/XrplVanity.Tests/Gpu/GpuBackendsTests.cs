using System;
using System.Collections.Generic;
using XrplVanity.Gpu;
using XrplVanity.Gpu.OpenCl;
using Xunit;

namespace XrplVanity.Tests.Gpu;

/// <summary>
/// Covers the fix for a Task 5 review finding: a constructor-time compilation failure inside a
/// backend's <see cref="IGpuBackend.Open"/> (CudaException from a module load, OpenClException
/// from a clBuildProgram failure) must surface through <see cref="GpuBackends.TryOpen(string, int, out IGpuDevice?, out string)"/>
/// and <see cref="GpuBackends.TryOpenFirst(out IGpuDevice?, out string)"/> as a clean
/// <c>false</c>/reason pair, not as an unhandled exception.
/// </summary>
public class GpuBackendsTests
{
    private sealed class ThrowingBackend : IGpuBackend
    {
        private readonly Func<Exception> _makeException;

        internal ThrowingBackend(string name, Func<Exception> makeException)
        {
            Name = name;
            _makeException = makeException;
        }

        public string Name { get; }

        public IReadOnlyList<GpuDeviceInfo> EnumerateDevices() => new[] { new GpuDeviceInfo(Name, 0, "fake device", "fake details") };

        public IGpuDevice Open(int index) => throw _makeException();
    }

    private static ThrowingBackend FakeOpenCl() => new(
        "fake-opencl",
        () => new OpenClException(-11, "clBuildProgram", "OpenCL program build failed:\nfake log line"));

    private static ThrowingBackend FakeCuda() => new(
        "fake-cuda",
        () => new CudaException(700, "cuModuleLoadDataEx", "fake CUDA module load failure"));

    [Fact]
    public void TryOpenFirst_Reports_OpenClException_Reason_Instead_Of_Crashing()
    {
        bool ok = GpuBackends.TryOpenFirst(new IGpuBackend[] { FakeOpenCl() }, out IGpuDevice? device, out string reason);

        Assert.False(ok);
        Assert.Null(device);
        Assert.Contains("fake log line", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TryOpen_Reports_OpenClException_Reason_Instead_Of_Crashing()
    {
        bool ok = GpuBackends.TryOpen(new IGpuBackend[] { FakeOpenCl() }, "fake-opencl", 0, out IGpuDevice? device, out string reason);

        Assert.False(ok);
        Assert.Null(device);
        Assert.Contains("fake log line", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TryOpenFirst_Reports_CudaException_Reason_Instead_Of_Crashing()
    {
        bool ok = GpuBackends.TryOpenFirst(new IGpuBackend[] { FakeCuda() }, out IGpuDevice? device, out string reason);

        Assert.False(ok);
        Assert.Null(device);
        Assert.Contains("fake CUDA module load failure", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TryOpen_Reports_CudaException_Reason_Instead_Of_Crashing()
    {
        bool ok = GpuBackends.TryOpen(new IGpuBackend[] { FakeCuda() }, "fake-cuda", 0, out IGpuDevice? device, out string reason);

        Assert.False(ok);
        Assert.Null(device);
        Assert.Contains("fake CUDA module load failure", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Drives a real clBuildProgram failure (not a fake) through the OpenCL device constructor,
    /// then proves the driver/runtime is still usable afterwards - the constructor's cleanup path
    /// (release queue + context on failure) must not leave the ICD in a bad state for the next
    /// Open() call.
    /// </summary>
    [Fact]
    public void OpenClDevice_Constructor_Reports_Build_Failure_And_Leaves_The_Runtime_Usable()
    {
        if (!OpenClBackend.TryEnumerate(out List<(IntPtr Platform, IntPtr Device, GpuDeviceInfo Info)> devices, out string enumerateReason) || devices.Count == 0)
        {
            GpuTestHelper.SkipOrFail("opencl", enumerateReason);
        }

        (IntPtr _, IntPtr device, GpuDeviceInfo info) = devices[0];
        const string brokenSource = "__kernel void k(__global int* o) { o[0] = ; }";

        OpenClException ex = Assert.Throws<OpenClException>(() => new OpenClDevice(device, info, brokenSource));
        Assert.Contains("OpenCL program build failed", ex.Message, StringComparison.Ordinal);

        bool ok = GpuBackends.TryOpen("opencl", 0, out IGpuDevice? reopened, out string reason);
        Assert.True(ok, $"OpenCL device 0 must still open after a failed build: {reason}");
        reopened!.Dispose();
    }
}
