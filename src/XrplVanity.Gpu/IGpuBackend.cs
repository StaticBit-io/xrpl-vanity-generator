using System;
using System.Collections.Generic;

namespace XrplVanity.Gpu;

public sealed record GpuDeviceInfo(string Backend, int Index, string Name, string Details, bool IsCpuDevice = false);

public interface IGpuKernel
{
}

public interface IGpuBuffer : IDisposable
{
    long Size { get; }
    void CopyFrom(ReadOnlySpan<byte> source);
    void CopyTo(Span<byte> destination);
    void Clear();
}

/// <summary>One opened device with the kernel program loaded. Not thread-safe; one per thread.</summary>
public interface IGpuDevice : IDisposable
{
    string Backend { get; }
    string Name { get; }

    /// <summary>True for a CPU OpenCL device (pocl, Intel/AMD CPU runtimes); false for GPUs.</summary>
    bool IsCpuDevice { get; }
    IGpuKernel GetKernel(string name);
    IGpuBuffer Alloc(long bytes);

    /// <summary>
    /// Runs <paramref name="kernel"/> for at least <paramref name="threads"/> work-items in
    /// blocks of <paramref name="blockSize"/>; the device rounds up to whole blocks, so kernels
    /// keep their <c>if (i >= n) return;</c> guard. Arguments must match the kernel parameter
    /// list exactly: <see cref="IGpuBuffer"/> for pointers; <c>ulong</c>, <c>long</c>, <c>int</c>,
    /// <c>uint</c>, <c>byte</c> for scalars.
    /// </summary>
    void Launch(IGpuKernel kernel, int threads, int blockSize, params object[] args);

    void Synchronize();
}

public interface IGpuBackend
{
    string Name { get; }
    IReadOnlyList<GpuDeviceInfo> EnumerateDevices();
    IGpuDevice Open(int index);
}
