namespace Echo.Depth;

public sealed record DepthExecutionPlan(
    int ConcurrentImages,
    int WorkersPerImage,
    long EstimatedBytesPerImage,
    long MemoryBudgetBytes)
{
    public long DecodedImageCacheBytes =>
        Math.Clamp(MemoryBudgetBytes - EstimatedBytesPerImage * ConcurrentImages, 0, 256L * 1024 * 1024);
}
