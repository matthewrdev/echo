namespace Echo.Telemetry;

public interface IGpuTimingSource
{
    // Cumulative completed command-buffer time. Reading never synchronizes the GPU.
    double CompletedGpuMilliseconds
    {
        get;
    }
}
