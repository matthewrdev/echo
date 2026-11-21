namespace Echo.Meshing.Repair;

public sealed record SurfaceBoundaryReport(int Edges, double Perimeter, double Radius, bool Interior);
