using System;
using System.Collections.Generic;

namespace XrplVanity.Gpu.Cuda;

public sealed class CudaBackend : IGpuBackend
{
    public string Name => "cuda";

    public IReadOnlyList<GpuDeviceInfo> EnumerateDevices()
    {
        if (!CudaContext.TryEnumerate(out IReadOnlyList<CudaDeviceInfo> devices, out _))
        {
            return Array.Empty<GpuDeviceInfo>();
        }

        GpuDeviceInfo[] list = new GpuDeviceInfo[devices.Count];
        for (int i = 0; i < devices.Count; i++)
        {
            list[i] = new GpuDeviceInfo(Name, devices[i].Ordinal, devices[i].Name, $"sm_{devices[i].ComputeCapabilityMajor}{devices[i].ComputeCapabilityMinor}");
        }

        return list;
    }

    public IGpuDevice Open(int index)
    {
        if (!CudaContext.TryCreate(index, out CudaContext? context, out string reason))
        {
            if (reason.Contains("out of range", StringComparison.Ordinal))
            {
                throw new ArgumentOutOfRangeException(nameof(index), reason);
            }

            throw new InvalidOperationException($"CUDA is not available: {reason}");
        }

        return new CudaDevice(context!);
    }
}

internal sealed class CudaKernel : IGpuKernel
{
    internal CudaKernel(CudaFunction function)
    {
        Function = function;
    }

    internal CudaFunction Function { get; }
}

internal sealed class CudaBufferAdapter : IGpuBuffer
{
    internal CudaBufferAdapter(DeviceBuffer buffer)
    {
        Buffer = buffer;
    }

    internal DeviceBuffer Buffer { get; }
    public long Size => Buffer.Size;
    public void CopyFrom(ReadOnlySpan<byte> source) => Buffer.CopyFrom(source);
    public void CopyTo(Span<byte> destination) => Buffer.CopyTo(destination);
    public void Clear() => Buffer.Clear();
    public void Dispose() => Buffer.Dispose();
}

internal sealed class CudaDevice : IGpuDevice
{
    private readonly CudaContext _context;
    private readonly CudaModule _module;

    internal CudaDevice(CudaContext context)
    {
        _context = context;
        try
        {
            _module = context.LoadModule(GpuKernels.ReadPtx());
        }
        catch
        {
            // LoadModule failing leaves the object never fully constructed, so Dispose() is never
            // reachable for it; dispose the already-created context ourselves before rethrowing.
            _context.Dispose();
            throw;
        }
    }

    public string Backend => "cuda";
    public string Name => $"{_context.DeviceName} (sm_{_context.ComputeCapabilityMajor}{_context.ComputeCapabilityMinor})";
    public bool IsCpuDevice => false;

    public IGpuKernel GetKernel(string name) => new CudaKernel(_module.GetFunction(name));

    public IGpuBuffer Alloc(long bytes) => new CudaBufferAdapter(_context.Alloc(bytes));

    public void Launch(IGpuKernel kernel, int threads, int blockSize, params object[] args)
    {
        if (blockSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(blockSize), "blockSize must be positive.");
        }

        object[] unwrapped = new object[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            unwrapped[i] = args[i] is CudaBufferAdapter adapter ? adapter.Buffer : args[i];
        }

        int grid = (threads + blockSize - 1) / blockSize;
        _context.Launch(((CudaKernel)kernel).Function, grid, blockSize, unwrapped);
    }

    public void Synchronize() => _context.Synchronize();

    public void Dispose() => _context.Dispose();
}
