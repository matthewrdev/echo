namespace Echo.Depth;

internal static class DepthBatchSizing
{
    public static int Rows(int columns, int maximumPlanes)
    {
        return Math.Clamp(SampleLimit(maximumPlanes) / Math.Max(1, columns), 1, 8);
    }

    public static long WorkerBytes(int maximumWidth, int maximumPlanes, int refinementPlanes)
    {
        // A smaller pyramid level can fit more rows than the largest level.
        // Include pool rounding, both sample lists, packed uploads and native
        // input/output storage. A single wide row remains the minimum unit.
        var capacity = Math.Max(maximumWidth, SampleLimit(maximumPlanes));
        return capacity * (24L * maximumPlanes + 16L * refinementPlanes + 1024);
    }

    private static int SampleLimit(int maximumPlanes) => Math.Min(4096, Math.Max(1, 524288 / maximumPlanes));
}
