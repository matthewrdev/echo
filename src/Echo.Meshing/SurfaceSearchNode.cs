using Echo.Geometry;

namespace Echo.Meshing;

internal readonly record struct SurfaceSearchNode(V3 Minimum, V3 Maximum, int Start, int Count, int Left, int Right);
