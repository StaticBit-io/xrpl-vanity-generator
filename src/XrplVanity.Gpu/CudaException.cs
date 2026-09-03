using System;

namespace XrplVanity.Gpu;

public sealed class CudaException : Exception
{
    public CudaException(int result, string operation, string message)
        : base($"{operation} failed with CUDA error {result}: {message}")
    {
        Result = result;
    }

    public int Result { get; }
}
