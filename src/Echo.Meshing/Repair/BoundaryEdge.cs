using Echo.Geometry;

namespace Echo.Meshing.Repair;

internal readonly record struct BoundaryEdge(int From, int To, V3 Normal, int Count, int Halfedge = -1);
