using Echo.Geometry;

namespace Echo.Meshing.Geometry;

internal readonly record struct SurfaceRay(
    V3 Start,
    V3 End);
