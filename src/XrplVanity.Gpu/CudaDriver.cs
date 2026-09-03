using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace XrplVanity.Gpu;

/// <summary>
/// Minimal CUDA driver API surface. The library name "cuda" is resolved at runtime to
/// nvcuda.dll (Windows) or libcuda.so.1 (Linux), so the assembly loads on machines
/// without any NVIDIA driver; calls fail only when actually made.
/// </summary>
internal static class CudaDriver
{
    private const string Lib = "cuda";

    internal const int CudaSuccess = 0;
    internal const int AttributeComputeCapabilityMajor = 75;
    internal const int AttributeComputeCapabilityMinor = 76;

    static CudaDriver()
    {
        NativeLibrary.SetDllImportResolver(typeof(CudaDriver).Assembly, Resolve);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != Lib)
        {
            return IntPtr.Zero;
        }

        string native = OperatingSystem.IsWindows() ? "nvcuda.dll" : "libcuda.so.1";
        return NativeLibrary.TryLoad(native, out IntPtr handle) ? handle : IntPtr.Zero;
    }

    [DllImport(Lib)] internal static extern int cuInit(uint flags);
    [DllImport(Lib)] internal static extern int cuDeviceGetCount(out int count);
    [DllImport(Lib)] internal static extern int cuDeviceGet(out int device, int ordinal);
    [DllImport(Lib)] internal static extern int cuDeviceGetName([Out] byte[] name, int length, int device);
    [DllImport(Lib)] internal static extern int cuDeviceGetAttribute(out int value, int attribute, int device);
    [DllImport(Lib, EntryPoint = "cuCtxCreate_v2")] internal static extern int cuCtxCreate(out IntPtr context, uint flags, int device);
    [DllImport(Lib, EntryPoint = "cuCtxDestroy_v2")] internal static extern int cuCtxDestroy(IntPtr context);
    [DllImport(Lib)] internal static extern int cuModuleLoadDataEx(out IntPtr module, byte[] image, uint numOptions, IntPtr options, IntPtr optionValues);
    [DllImport(Lib)] internal static extern int cuModuleGetFunction(out IntPtr function, IntPtr module, [MarshalAs(UnmanagedType.LPStr)] string name);
    [DllImport(Lib, EntryPoint = "cuMemAlloc_v2")] internal static extern int cuMemAlloc(out ulong pointer, nuint bytes);
    [DllImport(Lib, EntryPoint = "cuMemFree_v2")] internal static extern int cuMemFree(ulong pointer);
    [DllImport(Lib, EntryPoint = "cuMemcpyHtoD_v2")] internal static extern int cuMemcpyHtoD(ulong destination, IntPtr source, nuint bytes);
    [DllImport(Lib, EntryPoint = "cuMemcpyDtoH_v2")] internal static extern int cuMemcpyDtoH(IntPtr destination, ulong source, nuint bytes);
    [DllImport(Lib, EntryPoint = "cuMemsetD8_v2")] internal static extern int cuMemsetD8(ulong destination, byte value, nuint count);
    [DllImport(Lib)] internal static extern int cuLaunchKernel(IntPtr function, uint gridX, uint gridY, uint gridZ, uint blockX, uint blockY, uint blockZ, uint sharedMemBytes, IntPtr stream, IntPtr[] kernelParams, IntPtr extra);
    [DllImport(Lib)] internal static extern int cuCtxSynchronize();
    [DllImport(Lib)] internal static extern int cuGetErrorString(int result, out IntPtr text);

    internal static void Check(int result, string operation)
    {
        if (result == CudaSuccess)
        {
            return;
        }

        string message = "unknown error";
        try
        {
            if (cuGetErrorString(result, out IntPtr text) == CudaSuccess && text != IntPtr.Zero)
            {
                message = Marshal.PtrToStringAnsi(text) ?? message;
            }
        }
        catch (DllNotFoundException)
        {
            // The driver itself is missing; keep the generic message.
        }

        throw new CudaException(result, operation, message);
    }
}
