namespace Echo.Depth;

public sealed record DepthLevelMetrics(string ImageId, int Width, int Height, int Neighbours,
    double PreparationSeconds, double SearchSeconds, double FilterSeconds);
