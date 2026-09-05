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

    /// <summary>Backend whose <see cref="Open"/> succeeds and hands back a no-op device, unlike
    /// <see cref="ThrowingBackend"/> above (which always fails) — needed to exercise the "opened
    /// successfully" path of <see cref="GpuBackends.TryOpenFirst(IReadOnlyList{IGpuBackend}, out IGpuDevice?, out DeviceSelector?, out string)"/>.</summary>
    private sealed class FakeBackend : IGpuBackend
    {
        private readonly int _deviceCount;

        public FakeBackend(string name, int deviceCount)
        {
            Name = name;
            _deviceCount = deviceCount;
        }

        public string Name { get; }

        public IReadOnlyList<GpuDeviceInfo> EnumerateDevices()
        {
            GpuDeviceInfo[] list = new GpuDeviceInfo[_deviceCount];
            for (int i = 0; i < _deviceCount; i++)
            {
                list[i] = new GpuDeviceInfo(Name, i, $"{Name} device {i}", "fake");
            }

            return list;
        }

        public IGpuDevice Open(int index) => new FakeGpuDevice(Name);
    }

    private sealed class FakeGpuDevice : IGpuDevice
    {
        public FakeGpuDevice(string backend)
        {
            Backend = backend;
            Name = $"{backend} fake device";
        }

        public string Backend { get; }
        public string Name { get; }
        public bool IsCpuDevice => false;
        public IGpuKernel GetKernel(string name) => throw new NotSupportedException();
        public IGpuBuffer Alloc(long bytes) => throw new NotSupportedException();
        public void Launch(IGpuKernel kernel, int threads, int blockSize, params object[] args) => throw new NotSupportedException();
        public void Synchronize() { }
        public void Dispose() { }
    }

    [Fact]
    public void TryOpenFirst_Reports_The_Selector_Of_The_Device_It_Opened()
    {
        IGpuBackend[] backends = { new FakeBackend("cuda", deviceCount: 0), new FakeBackend("opencl", deviceCount: 2) };

        bool opened = GpuBackends.TryOpenFirst(backends, out IGpuDevice? device, out DeviceSelector? selector, out string reason);

        Assert.True(opened, reason);
        Assert.Equal(new DeviceSelector("opencl", 0), selector);
        device!.Dispose();
    }
}
