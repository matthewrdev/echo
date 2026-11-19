using Echo.Contracts;

namespace Echo.Depth;

internal static class DepthMemoryBudget
{
    public static int ResolveMegabytes(int configuredMegabytes, long availableBytes)
    {
        if (configuredMegabytes != 0)
        {
            return configuredMegabytes;
        }

        return (int)Math.Min(int.MaxValue, AutomaticMemoryBudget.ResolveBytes(availableBytes) / (1024 * 1024));
    }
}
