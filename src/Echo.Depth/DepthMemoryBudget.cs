namespace Echo.Depth;

internal static class DepthMemoryBudget
{
    public static int ResolveMegabytes(int configuredMegabytes, long availableBytes)
    {
        if (configuredMegabytes != 0)
        {
            return configuredMegabytes;
        }

        // Runtime capacity respects GC/container limits; it is not a live free-RAM reading.
        // Leave three quarters for the rest of the process and other applications.
        if (availableBytes <= 0)
        {
            return 1024;
        }

        return (int)Math.Clamp(availableBytes / 4 / (1024 * 1024), 256, 32768);
    }
}
