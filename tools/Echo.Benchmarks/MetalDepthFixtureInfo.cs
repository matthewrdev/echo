namespace Echo.Benchmarks;

internal sealed record MetalDepthFixtureInfo(int Width, int Height, int PixelCount, int PlaneCount, int ViewCount, string ImageId, double[] CpuSeconds);
