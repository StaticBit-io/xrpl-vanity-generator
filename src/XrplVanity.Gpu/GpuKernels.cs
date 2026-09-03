using System.IO;
using System.Reflection;

namespace XrplVanity.Gpu;

public static class GpuKernels
{
    private const string ResourceName = "xrpl_vanity.ptx";

    /// <summary>The PTX generated from gpu/kernels/xrpl_vanity.cu, embedded at build time.</summary>
    public static string ReadPtx()
    {
        Assembly assembly = typeof(GpuKernels).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new FileNotFoundException($"Embedded resource '{ResourceName}' is missing; run gpu/build-ptx.py and rebuild.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
