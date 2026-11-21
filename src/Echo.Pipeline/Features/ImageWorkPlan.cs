namespace Echo.Pipeline.Features;

public sealed record ImageWorkPlan(
    int Workers,
    long EstimatedBytesPerWorker,
    long FeatureCacheBytes);
