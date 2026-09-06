using System;
using System.Collections.Generic;
using XrplVanity.Gpu;

namespace XrplVanity.Search;

public sealed record DeviceCatalogEntry(DeviceSelector Selector, string Name, string Details, bool IsCpuDevice)
{
    public string Id => Selector.ToString();
}

/// <summary>Every device of every backend, in backend preference order, for front ends to offer.</summary>
public static class DeviceCatalog
{
    /// <summary>
    /// Enumerates every device of every backend. This can take seconds: the first call
    /// initialises the CUDA driver and every installed OpenCL platform, and an ICD loader that
    /// probes hardware — or a backend that is installed but broken — blocks until its driver
    /// gives up. Never call it on a UI thread; run it on a background thread and bind the result
    /// when it arrives.
    /// </summary>
    public static IReadOnlyList<DeviceCatalogEntry> Enumerate() => Enumerate(GpuBackends.All);

    internal static IReadOnlyList<DeviceCatalogEntry> Enumerate(IReadOnlyList<IGpuBackend> backends)
    {
        ArgumentNullException.ThrowIfNull(backends);
        List<DeviceCatalogEntry> entries = new();
        foreach (IGpuBackend backend in backends)
        {
            IReadOnlyList<GpuDeviceInfo> devices;
            try
            {
                devices = backend.EnumerateDevices();
            }
            catch (Exception)
            {
                // A backend whose driver or ICD loader is missing must not hide the others.
                continue;
            }

            foreach (GpuDeviceInfo info in devices)
            {
                entries.Add(new DeviceCatalogEntry(new DeviceSelector(backend.Name, info.Index), info.Name, info.Details, info.IsCpuDevice));
            }
        }

        return entries;
    }
}
