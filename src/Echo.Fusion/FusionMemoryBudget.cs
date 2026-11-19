using Echo.Contracts;

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
        // Use the same workstation capacity policy as the run and depth planners.
        // An explicit budget is independent of the resolution/detail multiplier.
        var budget = requestedMegabytes == 0
            ? Math.Min(AutomaticMemoryBudget.ResolveBytes(availableBytes), 524287 * megabyte)
            : requestedMegabytes * megabyte;
        // Conservative scratch estimate for occupied/candidate hash sets, sorted
        // keys, output references and samples. Source maps/cloud are separate.
        return new FusionResourcePlan(budget, checked((int)(budget / 256)));
    }
}
