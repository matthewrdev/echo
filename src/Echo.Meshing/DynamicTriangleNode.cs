using Echo.Geometry;

namespace Echo.Meshing;

internal readonly record struct DynamicTriangleNode(SurfaceTriangle Triangle, V3 Minimum, V3 Maximum, int Left, int Right, int Height);
