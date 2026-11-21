using Echo.Geometry;

namespace Echo.SurfaceCompletion;

public readonly record struct OrientedSurfaceSample(
    V3 Position,
    V3 Normal);
