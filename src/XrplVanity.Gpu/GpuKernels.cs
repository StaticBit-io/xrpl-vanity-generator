using System;
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

    /// <summary>The exact source build-ptx.py hands to NVRTC: cuda_shim.h, a newline, the body.</summary>
    public static string ReadCudaSource() => ReadResource("cuda_shim.h") + "\n" + ReadResource("xrpl_vanity_body.h");

    /// <summary>The SHA-256 stamped into the first line of the embedded PTX by build-ptx.py, or null when absent.</summary>
    public static string? ReadPtxBodyHash()
    {
        string ptx = ReadPtx();
        const string prefix = "// body-sha256: ";
        if (!ptx.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        int end = ptx.IndexOf('\n', prefix.Length);
        string hex = (end < 0 ? ptx[prefix.Length..] : ptx[prefix.Length..end]).Trim();
        return hex.Length == 64 ? hex : null;
    }

    /// <summary>SHA-256 (lower-case hex) of the CUDA source with CRLF normalised to LF — the same value build-ptx.py stamps.</summary>
    public static string ComputeBodyHash(string cudaSource)
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(cudaSource.Replace("\r\n", "\n", StringComparison.Ordinal));
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
    }

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
