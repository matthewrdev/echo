using Echo.Geometry;

namespace Echo.Depth;

internal readonly record struct DepthCoverageSample(
    V3 Position,
    double Weight,
    int RequiredViews);
