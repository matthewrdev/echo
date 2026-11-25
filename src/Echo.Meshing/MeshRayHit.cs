using Echo.Geometry;

namespace Echo.Meshing;

public readonly record struct MeshRayHit(V3 Position, V3 Normal, double Depth);
