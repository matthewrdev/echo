namespace Echo.Pipeline;

public sealed record ImageWorkPlan(int Workers, long EstimatedBytesPerWorker, long FeatureCacheBytes);
