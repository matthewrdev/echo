using Echo.Geometry;

namespace Echo.Benchmarks;

internal sealed record MetalDepthSample(
    V3 Ray,
    double[] Patch,
    double Norm);
