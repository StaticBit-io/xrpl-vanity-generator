using System;
using System.Runtime.InteropServices;
using System.Text;

namespace XrplVanity.Gpu;

/// <summary>One CUDA context on device 0. Not thread-safe; one per thread.</summary>
public sealed class CudaContext : IDisposable
{
    private readonly IntPtr _handle;
    private bool _disposed;

    private CudaContext(IntPtr handle, string deviceName, int major, int minor)
    {
        _handle = handle;
        DeviceName = deviceName;
        ComputeCapabilityMajor = major;
        ComputeCapabilityMinor = minor;
    }

    public string DeviceName { get; }
    public int ComputeCapabilityMajor { get; }
    public int ComputeCapabilityMinor { get; }

    /// <summary>Creates a context on device 0; returns false with a reason instead of throwing.</summary>
    public static bool TryCreate(out CudaContext? context, out string reason)
    {
        context = null;
        try
        {
            int result = CudaDriver.cuInit(0);
            if (result != CudaDriver.CudaSuccess)
            {
                reason = $"cuInit failed with CUDA error {result}";
                return false;
            }

            CudaDriver.Check(CudaDriver.cuDeviceGetCount(out int count), "cuDeviceGetCount");
            if (count == 0)
            {
                reason = "no CUDA devices";
                return false;
            }

            CudaDriver.Check(CudaDriver.cuDeviceGet(out int device, 0), "cuDeviceGet");
            byte[] nameBytes = new byte[256];
            CudaDriver.Check(CudaDriver.cuDeviceGetName(nameBytes, nameBytes.Length, device), "cuDeviceGetName");
            string name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
            CudaDriver.Check(CudaDriver.cuDeviceGetAttribute(out int major, CudaDriver.AttributeComputeCapabilityMajor, device), "cuDeviceGetAttribute(major)");
            CudaDriver.Check(CudaDriver.cuDeviceGetAttribute(out int minor, CudaDriver.AttributeComputeCapabilityMinor, device), "cuDeviceGetAttribute(minor)");
            CudaDriver.Check(CudaDriver.cuCtxCreate(out IntPtr handle, 0, device), "cuCtxCreate");

            context = new CudaContext(handle, name, major, minor);
            reason = string.Empty;
            return true;
        }
        catch (DllNotFoundException)
        {
            reason = "CUDA driver library not found (nvcuda.dll / libcuda.so.1)";
            return false;
        }
        catch (CudaException ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    public CudaModule LoadModule(string ptx)
    {
        byte[] image = Encoding.ASCII.GetBytes(ptx + "\0");
        CudaDriver.Check(CudaDriver.cuModuleLoadDataEx(out IntPtr module, image, 0, IntPtr.Zero, IntPtr.Zero), "cuModuleLoadDataEx");
        return new CudaModule(module);
    }

    public DeviceBuffer Alloc(long bytes)
    {
        CudaDriver.Check(CudaDriver.cuMemAlloc(out ulong pointer, (nuint)bytes), "cuMemAlloc");
        return new DeviceBuffer(pointer, bytes);
    }

    /// <summary>
    /// Launches a 1-D grid. Each argument must match the kernel parameter type exactly:
    /// a <see cref="DeviceBuffer"/> (or <c>ulong</c>) for a pointer, <c>int</c>/<c>uint</c>/
    /// <c>long</c>/<c>ulong</c>/<c>byte</c> for scalars.
    /// </summary>
    public void Launch(CudaFunction function, int gridX, int blockX, params object[] args)
    {
        GCHandle[] handles = new GCHandle[args.Length];
        IntPtr[] pointers = new IntPtr[args.Length];
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                object value = args[i] switch
                {
                    DeviceBuffer buffer => buffer.Pointer,
                    ulong or long or int or uint or byte => args[i],
                    _ => throw new ArgumentException($"Unsupported kernel argument type {args[i]?.GetType().Name ?? "null"} at index {i}."),
                };
                handles[i] = GCHandle.Alloc(value, GCHandleType.Pinned);
                pointers[i] = handles[i].AddrOfPinnedObject();
            }

            CudaDriver.Check(
                CudaDriver.cuLaunchKernel(function.Handle, (uint)gridX, 1, 1, (uint)blockX, 1, 1, 0, IntPtr.Zero, pointers, IntPtr.Zero),
                "cuLaunchKernel");
        }
        finally
        {
            foreach (GCHandle handle in handles)
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }
        }
    }

    public void Synchronize()
    {
        CudaDriver.Check(CudaDriver.cuCtxSynchronize(), "cuCtxSynchronize");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = CudaDriver.cuCtxDestroy(_handle);
    }
}
