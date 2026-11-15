namespace Echo.Fusion;

internal static class FusionMemoryBudget
{
    public static FusionResourcePlan Resolve(double detail, long availableBytes)
    {
        var invalidDetail = !double.IsFinite(detail) || detail < .25 || detail > 16;
        if (invalidDetail)
        {
            throw new ArgumentOutOfRangeException(nameof(detail));
        }

        const long megabyte = 1024 * 1024;
        var capacity = availableBytes > 0 ? availableBytes : 8L * 1024 * megabyte;
        var baseline = Math.Clamp(capacity / 8, 256 * megabyte, 4096 * megabyte);
        var maximum = Math.Max(64 * megabyte, Math.Min(capacity / 4, 32768 * megabyte));
        var budget = (long)Math.Clamp(baseline * detail, 64 * megabyte, maximum);
        // Conservative scratch estimate for occupied/candidate hash sets, sorted
        // keys, output references and samples. Source maps/cloud are separate.
        return new FusionResourcePlan(budget, checked((int)(budget / 256)));
    }
}
