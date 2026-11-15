namespace Echo.Fusion;

internal static class FusionMemoryBudget
{
    public static FusionResourcePlan Resolve(int requestedMegabytes, long availableBytes)
    {
        if (requestedMegabytes != 0 && requestedMegabytes is < 64 or > 524287)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedMegabytes));
        }

        const long megabyte = 1024 * 1024;
        var capacity = availableBytes > 0 ? availableBytes : 8L * 1024 * megabyte;
        // Automatic leaves room for source maps, the cloud and other applications.
        // An explicit budget is independent of the resolution/detail multiplier.
        var budget = requestedMegabytes == 0
            ? Math.Clamp(capacity / 4, 64 * megabyte, 524287 * megabyte)
            : requestedMegabytes * megabyte;
        // Conservative scratch estimate for occupied/candidate hash sets, sorted
        // keys, output references and samples. Source maps/cloud are separate.
        return new FusionResourcePlan(budget, checked((int)(budget / 256)));
    }
}
