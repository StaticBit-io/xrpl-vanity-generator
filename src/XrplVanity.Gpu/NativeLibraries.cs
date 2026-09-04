using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace XrplVanity.Gpu;

/// <summary>
/// Installs the single <see cref="DllImportResolver"/> allowed per assembly for
/// <c>XrplVanity.Gpu</c>. <see cref="NativeLibrary.SetDllImportResolver"/> throws if called
/// twice on the same assembly, and both <c>CudaDriver</c> ("cuda") and
/// <c>XrplVanity.Gpu.OpenCl.OpenClNative</c> ("opencl") need one, so both route through here
/// instead of registering their own.
/// </summary>
internal static class NativeLibraries
{
    private static readonly object Gate = new();
    private static bool _installed;

    internal static void EnsureResolverInstalled()
    {
        lock (Gate)
        {
            if (_installed)
            {
                return;
            }

            NativeLibrary.SetDllImportResolver(typeof(NativeLibraries).Assembly, Resolve);
            _installed = true;
        }
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        string? native = libraryName switch
        {
            "cuda" => OperatingSystem.IsWindows() ? "nvcuda.dll" : "libcuda.so.1",
            "opencl" => OperatingSystem.IsWindows() ? "OpenCL.dll" : "libOpenCL.so.1",
            _ => null,
        };

        if (native is null)
        {
            return IntPtr.Zero;
        }

        return NativeLibrary.TryLoad(native, out IntPtr handle) ? handle : IntPtr.Zero;
    }
}
