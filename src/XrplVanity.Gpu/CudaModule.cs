using System;

namespace XrplVanity.Gpu;

public sealed class CudaFunction
{
    internal CudaFunction(IntPtr handle)
    {
        Handle = handle;
    }

    internal IntPtr Handle { get; }
}

public sealed class CudaModule
{
    private readonly IntPtr _handle;

    internal CudaModule(IntPtr handle)
    {
        _handle = handle;
    }

    public CudaFunction GetFunction(string name)
    {
        CudaDriver.Check(CudaDriver.cuModuleGetFunction(out IntPtr function, _handle, name), $"cuModuleGetFunction({name})");
        return new CudaFunction(function);
    }
}
