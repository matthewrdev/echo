using Echo.Geometry;

namespace Echo.Meshing.Geometry;

internal readonly record struct SurfaceClosestPoint(V3 Position, V3 Normal, double DistanceSquared);
