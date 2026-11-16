namespace Echo.Fusion;

internal static class FusionGridPlanning
{
    public static FusionGridPlanningOptions Resolve(FusionResourcePlan resources, int pointCount)
    {
        // Account for hash-table slack, sorting arrays, point input, and up to
        // eight sequential merge readers per worker. Do not scale with the cloud.
        var memory = Math.Clamp(resources.MemoryBudgetBytes / 4, 1024 * 1024, 256L * 1024 * 1024);
        var usefulWorkers = Math.Max(1, (pointCount + 32767L) / 32768);
        var workers = (int)Math.Max(1, Math.Min(Environment.ProcessorCount, Math.Min(usefulWorkers, memory / (4 * 1024 * 1024))));
        var capacity = (int)Math.Clamp((memory / workers - 1024 * 1024) / 96, 1024, 262144);
        return new FusionGridPlanningOptions(workers, capacity);
    }
}
