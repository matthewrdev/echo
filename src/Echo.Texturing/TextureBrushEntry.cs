using Echo.Geometry;

namespace Echo.Texturing;

internal sealed record TextureBrushEntry(V3 Centre, V3 Normal, double Radius, double Depth, int Stroke);
