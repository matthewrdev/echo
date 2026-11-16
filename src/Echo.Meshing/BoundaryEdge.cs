using Echo.Geometry;

namespace Echo.Meshing;

internal readonly record struct BoundaryEdge(int From, int To, V3 Normal, int Count, int Halfedge = -1);
