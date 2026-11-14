namespace Echo.Depth;

public sealed record DepthExecutionPlan(int ConcurrentImages, int WorkersPerImage, long EstimatedBytesPerImage, long MemoryBudgetBytes);
