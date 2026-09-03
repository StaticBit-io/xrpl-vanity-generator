using System;
using System.Collections.Generic;
using XrplVanity.Gpu.Cuda;

namespace XrplVanity.Gpu;

public static class GpuBackends
{
    /// <summary>Preference order: CUDA first (verified, fastest on NVIDIA), then OpenCL (Task 3).</summary>
    public static IReadOnlyList<IGpuBackend> All { get; } = new IGpuBackend[] { new CudaBackend() };

    public static IGpuBackend? Get(string name)
    {
        foreach (IGpuBackend backend in All)
        {
            if (string.Equals(backend.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return backend;
            }
        }

        return null;
    }

    public static bool TryOpenFirst(out IGpuDevice? device, out string reason)
    {
        List<string> reasons = new();
        foreach (IGpuBackend backend in All)
        {
            if (backend.EnumerateDevices().Count > 0)
            {
                try
                {
                    device = backend.Open(0);
                    reason = string.Empty;
                    return true;
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
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

    public static bool TryOpen(string backendName, int index, out IGpuDevice? device, out string reason)
    {
        IGpuBackend? backend = Get(backendName);
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
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            device = null;
            reason = ex.Message;
            return false;
        }
    }
}
