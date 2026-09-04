namespace XrplVanity.Gpu;

public static class GpuAvailability
{
    /// <summary>Probes for a usable GPU device (CUDA preferred, then OpenCL) without throwing.</summary>
    public static (bool Available, string Info) Detect()
    {
        if (!GpuBackends.TryOpenFirst(out IGpuDevice? device, out string reason))
        {
            return (false, reason);
        }

        using (device)
        {
            return (true, $"{device!.Backend}:{device.Name}");
        }
    }
}
