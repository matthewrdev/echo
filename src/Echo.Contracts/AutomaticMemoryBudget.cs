namespace Echo.Contracts;

public static class AutomaticMemoryBudget
{
    public static long ResolveBytes(long capacityBytes)
    {
        const long megabyte = 1024 * 1024;
        if (capacityBytes <= 0)
        {
            return 1024 * megabyte;
        }

        // Capacity respects runtime/container limits. Allow processing to use
        // seven eighths, retaining OS headroom without a fixed workstation cap.
        return Math.Max(256 * megabyte, capacityBytes - capacityBytes / 8);
    }
}
