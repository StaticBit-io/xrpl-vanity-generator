using System;

namespace XrplVanity.Gpu;

public sealed class DeviceBuffer : IDisposable
{
    private bool _disposed;

    internal DeviceBuffer(ulong pointer, long size)
    {
        Pointer = pointer;
        Size = size;
    }

    public ulong Pointer { get; }
    public long Size { get; }

    public unsafe void CopyFrom(ReadOnlySpan<byte> source)
    {
        EnsureFits(source.Length);
        fixed (byte* p = source)
        {
            CudaDriver.Check(CudaDriver.cuMemcpyHtoD(Pointer, (IntPtr)p, (nuint)source.Length), "cuMemcpyHtoD");
        }
    }

    public unsafe void CopyTo(Span<byte> destination)
    {
        EnsureFits(destination.Length);
        fixed (byte* p = destination)
        {
            CudaDriver.Check(CudaDriver.cuMemcpyDtoH((IntPtr)p, Pointer, (nuint)destination.Length), "cuMemcpyDtoH");
        }
    }

    public void Clear()
    {
        CudaDriver.Check(CudaDriver.cuMemsetD8(Pointer, 0, (nuint)Size), "cuMemsetD8");
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
        _ = CudaDriver.cuMemFree(Pointer);
    }
}
