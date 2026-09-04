using System;
using System.Collections.Generic;
using XrplVanity.Gpu.Cuda;
using XrplVanity.Gpu.OpenCl;

namespace XrplVanity.Gpu;

public static class GpuBackends
{
    /// <summary>Preference order: CUDA first (verified, fastest on NVIDIA), then OpenCL.</summary>
    public static IReadOnlyList<IGpuBackend> All { get; } = new IGpuBackend[] { new CudaBackend(), new OpenClBackend() };

    public static bool TryOpenFirst(out IGpuDevice? device, out string reason) => TryOpenFirst(All, out device, out reason);

    public static bool TryOpen(string backendName, int index, out IGpuDevice? device, out string reason) =>
        TryOpen(All, backendName, index, out device, out reason);

    /// <summary>
    /// Testable core of <see cref="TryOpenFirst(out IGpuDevice?, out string)"/>: takes the backend
    /// list as a parameter so tests can substitute a fake backend that fails deterministically.
    /// </summary>
    internal static bool TryOpenFirst(IReadOnlyList<IGpuBackend> backends, out IGpuDevice? device, out string reason)
    {
        List<string> reasons = new();
        foreach (IGpuBackend backend in backends)
        {
            if (backend.EnumerateDevices().Count > 0)
            {
                try
                {
                    device = backend.Open(0);
                    reason = string.Empty;
                    return true;
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException or CudaException or OpenClException)
                {
                    reasons.Add($"{backend.Name}: {ex.Message}");
                }
            }
            else
            {
                reasons.Add($"{backend.Name}: no devices");
            }
        }

        device = null;
        reason = string.Join("; ", reasons);
        return false;
    }

    /// <summary>
    /// Testable core of <see cref="TryOpen(string, int, out IGpuDevice?, out string)"/>: takes the
    /// backend list as a parameter so tests can substitute a fake backend that fails deterministically.
    /// </summary>
    internal static bool TryOpen(IReadOnlyList<IGpuBackend> backends, string backendName, int index, out IGpuDevice? device, out string reason)
    {
        IGpuBackend? backend = null;
        foreach (IGpuBackend candidate in backends)
        {
            if (string.Equals(candidate.Name, backendName, StringComparison.OrdinalIgnoreCase))
            {
                backend = candidate;
                break;
            }
        }

        if (backend is null)
        {
            device = null;
            reason = $"unknown backend '{backendName}' (known: cuda, opencl)";
            return false;
        }

        try
        {
            device = backend.Open(index);
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException or CudaException or OpenClException)
        {
            device = null;
            reason = ex.Message;
            return false;
        }
    }
}
