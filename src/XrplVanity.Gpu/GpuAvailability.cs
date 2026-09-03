namespace XrplVanity.Gpu;

public static class GpuAvailability
{
    /// <summary>Probes for a usable CUDA device without throwing.</summary>
    public static (bool Available, string Info) Detect()
    {
        if (!CudaContext.TryCreate(out CudaContext? context, out string reason))
        {
            return (false, reason);
        }

        using (context)
        {
            return (true, $"{context!.DeviceName} (sm_{context.ComputeCapabilityMajor}{context.ComputeCapabilityMinor})");
        }
    }
}
