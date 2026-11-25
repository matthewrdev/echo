namespace Echo.ImageProjection;

public sealed record ImageGeometrySample(
    double[] Pixel, double[]? Position, double[]? Normal, bool BreakBefore);
