using System.Numerics;

namespace Echo.Fusion;

internal static class FusionIntegrationPlanning
{
    public static FusionIntegrationPlan Resolve(long memoryBytes, long computeBytes, long cells = long.MaxValue, long depths = long.MaxValue)
    {
        var budget = Math.Max(262144, Math.Min(memoryBytes / 8, computeBytes >= 262144 ? computeBytes : memoryBytes / 8));
        var batch = PowerOfTwo(Math.Clamp(budget / 256, 1024, 262144));
        var depthCapacity = PowerOfTwo(Math.Clamp(budget / 8, 4096, 32 * 1024 * 1024));
        batch = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Clamp(cells, 1, batch));
        depthCapacity = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Clamp(depths, 4, depthCapacity));
        // Resident maps are charged to the compute budget, with room for job,
        // accumulation and camera buffers. Host staging stays bounded separately.
        var residentBudget = Math.Min(memoryBytes / 2, computeBytes) - batch * 32L - 2 * 1024 * 1024;
        var canKeepDepthsResident = depths > 0 && depths <= int.MaxValue / sizeof(float) && depths <= residentBudget / sizeof(float);
        var resident = canKeepDepthsResident ? (int)depths : 0;
        var checkpoint = PowerOfTwo(Math.Clamp(memoryBytes / 32 / 20, batch, 4 * 1024 * 1024));
        checkpoint = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Clamp(cells, 1, checkpoint));
        return new FusionIntegrationPlan(batch, depthCapacity, resident, checkpoint);
    }

    private static int PowerOfTwo(long count) => 1 << BitOperations.Log2((uint)count);
}
