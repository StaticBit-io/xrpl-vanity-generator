using System.IO;
using System.Reflection;

namespace XrplVanity.Gpu;

public static class GpuKernels
{
    /// <summary>
    /// The PTX generated from gpu/kernels/xrpl_vanity_body.h + cuda_shim.h by
    /// gpu/build-ptx.py, embedded at build time.
    /// </summary>
    public static string ReadPtx() => ReadResource("xrpl_vanity.ptx", "run gpu/build-ptx.py and rebuild");

    /// <summary>The OpenCL C 1.2 program: opencl_shim.h followed by xrpl_vanity_body.h.</summary>
    public static string ReadOpenClSource() => ReadResource("opencl_shim.h") + "\n" + ReadResource("xrpl_vanity_body.h");

    private static string ReadResource(string name, string? missingHint = null)
    {
        Assembly assembly = typeof(GpuKernels).Assembly;
        string hint = missingHint is null ? string.Empty : $"; {missingHint}";
        using Stream? stream = assembly.GetManifestResourceStream(name)
            ?? throw new FileNotFoundException($"Embedded resource '{name}' is missing{hint}.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
