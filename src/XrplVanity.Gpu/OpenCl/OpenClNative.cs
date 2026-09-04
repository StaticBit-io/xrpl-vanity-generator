using System;
using System.Runtime.InteropServices;

namespace XrplVanity.Gpu.OpenCl;

/// <summary>OpenCL 1.2 surface over the ICD loader (OpenCL.dll on Windows, libOpenCL.so.1 on Linux).</summary>
internal static class OpenClNative
{
    private const string Lib = "opencl";

    internal const int Success = 0;
    internal const ulong DeviceTypeAll = 0xFFFFFFFF;
    internal const ulong MemReadWrite = 1;
    internal const uint PlatformVersion = 0x0901;
    internal const uint PlatformName = 0x0902;
    internal const uint DeviceName = 0x102B;
    internal const uint DeviceVersion = 0x102F;
    internal const uint ProgramBuildLog = 0x1183;

    static OpenClNative()
    {
        NativeLibraries.EnsureResolverInstalled();
    }

    [DllImport(Lib)] internal static extern int clGetPlatformIDs(uint numEntries, IntPtr[]? platforms, out uint numPlatforms);
    [DllImport(Lib)] internal static extern int clGetPlatformInfo(IntPtr platform, uint param, nuint size, byte[]? value, out nuint sizeRet);
    [DllImport(Lib)] internal static extern int clGetDeviceIDs(IntPtr platform, ulong deviceType, uint numEntries, IntPtr[]? devices, out uint numDevices);
    [DllImport(Lib)] internal static extern int clGetDeviceInfo(IntPtr device, uint param, nuint size, byte[]? value, out nuint sizeRet);
    [DllImport(Lib)] internal static extern IntPtr clCreateContext(IntPtr properties, uint numDevices, IntPtr[] devices, IntPtr callback, IntPtr userData, out int error);
    [DllImport(Lib)] internal static extern IntPtr clCreateCommandQueue(IntPtr context, IntPtr device, ulong properties, out int error);
    [DllImport(Lib)] internal static extern IntPtr clCreateProgramWithSource(IntPtr context, uint count, IntPtr[] strings, nuint[] lengths, out int error);
    [DllImport(Lib)] internal static extern int clBuildProgram(IntPtr program, uint numDevices, IntPtr[] devices, [MarshalAs(UnmanagedType.LPStr)] string options, IntPtr callback, IntPtr userData);
    [DllImport(Lib)] internal static extern int clGetProgramBuildInfo(IntPtr program, IntPtr device, uint param, nuint size, byte[]? value, out nuint sizeRet);
    [DllImport(Lib)] internal static extern IntPtr clCreateKernel(IntPtr program, [MarshalAs(UnmanagedType.LPStr)] string name, out int error);
    [DllImport(Lib)] internal static extern IntPtr clCreateBuffer(IntPtr context, ulong flags, nuint size, IntPtr hostPtr, out int error);
    [DllImport(Lib)] internal static extern int clEnqueueWriteBuffer(IntPtr queue, IntPtr buffer, uint blocking, nuint offset, nuint size, IntPtr ptr, uint numEvents, IntPtr events, IntPtr evt);
    [DllImport(Lib)] internal static extern int clEnqueueReadBuffer(IntPtr queue, IntPtr buffer, uint blocking, nuint offset, nuint size, IntPtr ptr, uint numEvents, IntPtr events, IntPtr evt);
    [DllImport(Lib)] internal static extern int clEnqueueFillBuffer(IntPtr queue, IntPtr buffer, IntPtr pattern, nuint patternSize, nuint offset, nuint size, uint numEvents, IntPtr events, IntPtr evt);
    [DllImport(Lib)] internal static extern int clSetKernelArg(IntPtr kernel, uint index, nuint size, IntPtr value);
    [DllImport(Lib)] internal static extern int clEnqueueNDRangeKernel(IntPtr queue, IntPtr kernel, uint dimensions, IntPtr globalOffset, nuint[] globalSize, nuint[] localSize, uint numEvents, IntPtr events, IntPtr evt);
    [DllImport(Lib)] internal static extern int clFinish(IntPtr queue);
    [DllImport(Lib)] internal static extern int clReleaseMemObject(IntPtr mem);
    [DllImport(Lib)] internal static extern int clReleaseKernel(IntPtr kernel);
    [DllImport(Lib)] internal static extern int clReleaseProgram(IntPtr program);
    [DllImport(Lib)] internal static extern int clReleaseCommandQueue(IntPtr queue);
    [DllImport(Lib)] internal static extern int clReleaseContext(IntPtr context);

    internal static void Check(int code, string operation, string detail = "")
    {
        if (code != Success)
        {
            throw new OpenClException(code, operation, detail.Length == 0 ? "see error name" : detail);
        }
    }

    internal static string ErrorName(int code) => code switch
    {
        0 => "CL_SUCCESS",
        -1 => "CL_DEVICE_NOT_FOUND",
        -2 => "CL_DEVICE_NOT_AVAILABLE",
        -3 => "CL_COMPILER_NOT_AVAILABLE",
        -4 => "CL_MEM_OBJECT_ALLOCATION_FAILURE",
        -5 => "CL_OUT_OF_RESOURCES",
        -6 => "CL_OUT_OF_HOST_MEMORY",
        -11 => "CL_BUILD_PROGRAM_FAILURE",
        -30 => "CL_INVALID_VALUE",
        -32 => "CL_INVALID_PLATFORM",
        -33 => "CL_INVALID_DEVICE",
        -34 => "CL_INVALID_CONTEXT",
        -36 => "CL_INVALID_COMMAND_QUEUE",
        -38 => "CL_INVALID_MEM_OBJECT",
        -44 => "CL_INVALID_PROGRAM",
        -45 => "CL_INVALID_PROGRAM_EXECUTABLE",
        -46 => "CL_INVALID_KERNEL_NAME",
        -48 => "CL_INVALID_KERNEL",
        -49 => "CL_INVALID_ARG_INDEX",
        -51 => "CL_INVALID_ARG_SIZE",
        -52 => "CL_INVALID_KERNEL_ARGS",
        -54 => "CL_INVALID_WORK_GROUP_SIZE",
        -61 => "CL_INVALID_BUFFER_SIZE",
        _ => "CL_UNKNOWN",
    };

    internal static string PlatformString(IntPtr platform, uint param)
    {
        Check(clGetPlatformInfo(platform, param, 0, null, out nuint size), "clGetPlatformInfo(size)");
        byte[] buffer = new byte[(int)size];
        Check(clGetPlatformInfo(platform, param, size, buffer, out _), "clGetPlatformInfo");
        return System.Text.Encoding.ASCII.GetString(buffer).TrimEnd('\0', ' ');
    }

    internal static string DeviceString(IntPtr device, uint param)
    {
        Check(clGetDeviceInfo(device, param, 0, null, out nuint size), "clGetDeviceInfo(size)");
        byte[] buffer = new byte[(int)size];
        Check(clGetDeviceInfo(device, param, size, buffer, out _), "clGetDeviceInfo");
        return System.Text.Encoding.ASCII.GetString(buffer).TrimEnd('\0', ' ');
    }
}
