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
