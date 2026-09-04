using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace XrplVanity.Gpu.OpenCl;

public sealed class OpenClBackend : IGpuBackend
{
    private const string BackendName = "opencl";

    public string Name => BackendName;

    /// <summary>
    /// Internal (not private) so tests can obtain a real device handle to construct
    /// <see cref="OpenClDevice"/> directly with a deliberately broken program source, exercising
    /// the build-failure cleanup path without needing a vendor compiler that can be made to fail
    /// on demand from the public API.
    /// </summary>
    internal static bool TryEnumerate(out List<(IntPtr Platform, IntPtr Device, GpuDeviceInfo Info)> devices, out string reason)
    {
        devices = new List<(IntPtr, IntPtr, GpuDeviceInfo)>();
        try
        {
            int rc = OpenClNative.clGetPlatformIDs(0, null, out uint platformCount);
            if (rc != OpenClNative.Success || platformCount == 0)
            {
                reason = rc == OpenClNative.Success ? "no OpenCL platforms" : $"clGetPlatformIDs: {OpenClNative.ErrorName(rc)}";
                return false;
            }

            IntPtr[] platforms = new IntPtr[platformCount];
            OpenClNative.Check(OpenClNative.clGetPlatformIDs(platformCount, platforms, out _), "clGetPlatformIDs");
            int index = 0;
            foreach (IntPtr platform in platforms)
            {
                string platformName = OpenClNative.PlatformString(platform, OpenClNative.PlatformName);
                int drc = OpenClNative.clGetDeviceIDs(platform, OpenClNative.DeviceTypeAll, 0, null, out uint deviceCount);
                if (drc != OpenClNative.Success || deviceCount == 0)
                {
                    continue; // a platform with no devices is not an error
                }

                IntPtr[] ids = new IntPtr[deviceCount];
                OpenClNative.Check(OpenClNative.clGetDeviceIDs(platform, OpenClNative.DeviceTypeAll, deviceCount, ids, out _), "clGetDeviceIDs");
                foreach (IntPtr device in ids)
                {
                    string name = OpenClNative.DeviceString(device, OpenClNative.DeviceName);
                    string version = OpenClNative.DeviceString(device, OpenClNative.DeviceVersion);
                    bool isCpu = (OpenClNative.DeviceUlong(device, OpenClNative.DeviceType) & OpenClNative.DeviceTypeCpu) != 0;
                    devices.Add((platform, device, new GpuDeviceInfo(BackendName, index, name, $"{platformName}, {version}", isCpu)));
                    index++;
                }
            }

            reason = devices.Count == 0 ? "no OpenCL devices" : string.Empty;
            return devices.Count > 0;
        }
        catch (DllNotFoundException)
        {
            reason = "OpenCL ICD loader not found (OpenCL.dll / libOpenCL.so.1)";
            return false;
        }
        catch (EntryPointNotFoundException ex)
        {
            reason = $"OpenCL ICD loader is missing an entry point: {ex.Message}";
            return false;
        }
        catch (OpenClException ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    public IReadOnlyList<GpuDeviceInfo> EnumerateDevices()
    {
        if (!TryEnumerate(out List<(IntPtr, IntPtr, GpuDeviceInfo Info)> devices, out _))
        {
            return Array.Empty<GpuDeviceInfo>();
        }

        List<GpuDeviceInfo> infos = new(devices.Count);
        foreach ((_, _, GpuDeviceInfo info) in devices)
        {
            infos.Add(info);
        }

        return infos;
    }

    public IGpuDevice Open(int index)
    {
        if (!TryEnumerate(out List<(IntPtr Platform, IntPtr Device, GpuDeviceInfo Info)> devices, out string reason))
        {
            throw new InvalidOperationException($"OpenCL is not available: {reason}");
        }

        if (index < 0 || index >= devices.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), $"OpenCL device index {index} is out of range (0..{devices.Count - 1}).");
        }

        (IntPtr _, IntPtr device, GpuDeviceInfo info) = devices[index];
        return new OpenClDevice(device, info);
    }
}

internal sealed class OpenClKernel : IGpuKernel
{
    internal OpenClKernel(IntPtr handle, nuint maxWorkGroupSize)
    {
        Handle = handle;
        MaxWorkGroupSize = maxWorkGroupSize;
    }

    internal IntPtr Handle { get; }
    internal nuint MaxWorkGroupSize { get; }
}

internal sealed class OpenClBuffer : IGpuBuffer
{
    private readonly IntPtr _queue;
    private bool _disposed;

    internal OpenClBuffer(IntPtr handle, IntPtr queue, long size)
    {
        Handle = handle;
        _queue = queue;
        Size = size;
    }

    internal IntPtr Handle { get; }
    public long Size { get; }

    public unsafe void CopyFrom(ReadOnlySpan<byte> source)
    {
        EnsureFits(source.Length);
        fixed (byte* p = source)
        {
            OpenClNative.Check(OpenClNative.clEnqueueWriteBuffer(_queue, Handle, 1, 0, (nuint)source.Length, (IntPtr)p, 0, IntPtr.Zero, IntPtr.Zero), "clEnqueueWriteBuffer");
        }
    }

    public unsafe void CopyTo(Span<byte> destination)
    {
        EnsureFits(destination.Length);
        fixed (byte* p = destination)
        {
            OpenClNative.Check(OpenClNative.clEnqueueReadBuffer(_queue, Handle, 1, 0, (nuint)destination.Length, (IntPtr)p, 0, IntPtr.Zero, IntPtr.Zero), "clEnqueueReadBuffer");
        }
    }

    public unsafe void Clear()
    {
        byte zero = 0;
        OpenClNative.Check(OpenClNative.clEnqueueFillBuffer(_queue, Handle, (IntPtr)(&zero), 1, 0, (nuint)Size, 0, IntPtr.Zero, IntPtr.Zero), "clEnqueueFillBuffer");
        OpenClNative.Check(OpenClNative.clFinish(_queue), "clFinish");
    }

    private void EnsureFits(int length)
    {
        if (length > Size)
        {
            throw new ArgumentOutOfRangeException(nameof(length), $"{length} bytes exceed the {Size}-byte device buffer.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = OpenClNative.clReleaseMemObject(Handle);
    }
}

internal sealed class OpenClDevice : IGpuDevice
{
    private readonly IntPtr _device;
    private readonly IntPtr _context;
    private readonly IntPtr _queue;
    private readonly IntPtr _program;
    private readonly nuint _deviceMaxWorkGroupSize;
    private readonly List<IntPtr> _kernels = new();
    private bool _disposed;

    internal OpenClDevice(IntPtr device, GpuDeviceInfo info)
        : this(device, info, GpuKernels.ReadOpenClSource())
    {
    }

    /// <summary>
    /// Takes the program source directly rather than reading the embedded kernel, so a test can
    /// exercise the build-failure cleanup path with a deliberately broken program.
    /// </summary>
    internal OpenClDevice(IntPtr device, GpuDeviceInfo info, string source)
    {
        _device = device;
        Name = $"{info.Name} ({info.Details})";
        IsCpuDevice = info.IsCpuDevice;
        _deviceMaxWorkGroupSize = OpenClNative.DeviceSize(device, OpenClNative.DeviceMaxWorkGroupSize);
        IntPtr[] devices = { device };
        _context = OpenClNative.clCreateContext(IntPtr.Zero, 1, devices, IntPtr.Zero, IntPtr.Zero, out int error);
        OpenClNative.Check(error, "clCreateContext");
        try
        {
            _queue = OpenClNative.clCreateCommandQueue(_context, device, 0, out error);
            OpenClNative.Check(error, "clCreateCommandQueue");
            _program = Build(source);
        }
        catch
        {
            // Release whatever was already acquired before rethrowing, so a failure partway
            // through construction (e.g. clCreateCommandQueue, or a clBuildProgram failure inside
            // Build) doesn't leak a context+queue pair: the object is never fully constructed, so
            // Dispose() is never reachable for it.
            if (_queue != IntPtr.Zero)
            {
                _ = OpenClNative.clReleaseCommandQueue(_queue);
            }

            _ = OpenClNative.clReleaseContext(_context);
            throw;
        }
    }

    public string Backend => "opencl";
    public string Name { get; }
    public bool IsCpuDevice { get; }

    private IntPtr Build(string source)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(source);
        GCHandle pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            IntPtr[] strings = { pin.AddrOfPinnedObject() };
            nuint[] lengths = { (nuint)bytes.Length };
            IntPtr program = OpenClNative.clCreateProgramWithSource(_context, 1, strings, lengths, out int error);
            OpenClNative.Check(error, "clCreateProgramWithSource");
            IntPtr[] devices = { _device };

            // clBuildProgram recurses deeply inside the vendor compiler while processing the
            // heavily-unrolled kernel body; on the default ~1 MB thread stack (what the .NET
            // thread pool and xUnit's test-execution threads use) this crashes the process with a
            // native "Stack overflow." before any managed exception can be raised. Running the
            // call on a dedicated thread with a large stack keeps compilation off that path.
            int rc = OpenClNative.Success;
            Exception? buildThreadException = null;
            Thread buildThread = new(() =>
            {
                try
                {
                    rc = OpenClNative.clBuildProgram(program, 1, devices, "-cl-std=CL1.2", IntPtr.Zero, IntPtr.Zero);
                }
                catch (Exception ex)
                {
                    buildThreadException = ex;
                }
            }, 64 * 1024 * 1024);
            buildThread.Start();
            buildThread.Join();

            if (buildThreadException is not null)
            {
                _ = OpenClNative.clReleaseProgram(program);
                throw buildThreadException;
            }

            if (rc != OpenClNative.Success)
            {
                string log = BuildLog(program);
                _ = OpenClNative.clReleaseProgram(program);
                throw new OpenClException(rc, "clBuildProgram", "OpenCL program build failed:\n" + log);
            }

            return program;
        }
        finally
        {
            pin.Free();
        }
    }

    private string BuildLog(IntPtr program)
    {
        if (OpenClNative.clGetProgramBuildInfo(program, _device, OpenClNative.ProgramBuildLog, 0, null, out nuint size) != OpenClNative.Success || size == 0)
        {
            return "<no build log>";
        }

        byte[] buffer = new byte[(int)size];
        _ = OpenClNative.clGetProgramBuildInfo(program, _device, OpenClNative.ProgramBuildLog, size, buffer, out _);
        return Encoding.ASCII.GetString(buffer).TrimEnd('\0');
    }

    public IGpuKernel GetKernel(string name)
    {
        IntPtr kernel = OpenClNative.clCreateKernel(_program, name, out int error);
        OpenClNative.Check(error, $"clCreateKernel({name})");
        _kernels.Add(kernel);
        nuint maxWorkGroupSize = OpenClNative.KernelMaxWorkGroupSize(kernel, _device);
        return new OpenClKernel(kernel, maxWorkGroupSize);
    }

    public IGpuBuffer Alloc(long bytes)
    {
        IntPtr handle = OpenClNative.clCreateBuffer(_context, OpenClNative.MemReadWrite, (nuint)bytes, IntPtr.Zero, out int error);
        OpenClNative.Check(error, "clCreateBuffer");
        return new OpenClBuffer(handle, _queue, bytes);
    }

    public void Launch(IGpuKernel kernel, int threads, int blockSize, params object[] args)
    {
        IntPtr handle = ((OpenClKernel)kernel).Handle;
        for (int i = 0; i < args.Length; i++)
        {
            SetArg(handle, (uint)i, args[i]);
        }

        nuint limit = Math.Min(((OpenClKernel)kernel).MaxWorkGroupSize, _deviceMaxWorkGroupSize);
        if (limit == 0) limit = 1;
        nuint local = Math.Min((nuint)blockSize, limit);
        nuint global = ((nuint)threads + local - 1) / local * local;
        OpenClNative.Check(
            OpenClNative.clEnqueueNDRangeKernel(_queue, handle, 1, IntPtr.Zero, new[] { global }, new[] { local }, 0, IntPtr.Zero, IntPtr.Zero),
            "clEnqueueNDRangeKernel");
    }

    private static unsafe void SetArg(IntPtr kernel, uint index, object value)
    {
        switch (value)
        {
            case OpenClBuffer buffer:
            {
                IntPtr mem = buffer.Handle;
                OpenClNative.Check(OpenClNative.clSetKernelArg(kernel, index, (nuint)IntPtr.Size, (IntPtr)(&mem)), $"clSetKernelArg({index})");
                break;
            }
            case ulong u64: OpenClNative.Check(OpenClNative.clSetKernelArg(kernel, index, 8, (IntPtr)(&u64)), $"clSetKernelArg({index})"); break;
            case long i64: OpenClNative.Check(OpenClNative.clSetKernelArg(kernel, index, 8, (IntPtr)(&i64)), $"clSetKernelArg({index})"); break;
            case int i32: OpenClNative.Check(OpenClNative.clSetKernelArg(kernel, index, 4, (IntPtr)(&i32)), $"clSetKernelArg({index})"); break;
            case uint u32: OpenClNative.Check(OpenClNative.clSetKernelArg(kernel, index, 4, (IntPtr)(&u32)), $"clSetKernelArg({index})"); break;
            case byte b: OpenClNative.Check(OpenClNative.clSetKernelArg(kernel, index, 1, (IntPtr)(&b)), $"clSetKernelArg({index})"); break;
            default: throw new ArgumentException($"Unsupported kernel argument type {value?.GetType().Name ?? "null"} at index {index}.");
        }
    }

    public void Synchronize()
    {
        OpenClNative.Check(OpenClNative.clFinish(_queue), "clFinish");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (IntPtr kernel in _kernels)
        {
            _ = OpenClNative.clReleaseKernel(kernel);
        }

        _ = OpenClNative.clReleaseProgram(_program);
        _ = OpenClNative.clReleaseCommandQueue(_queue);
        _ = OpenClNative.clReleaseContext(_context);
    }
}
