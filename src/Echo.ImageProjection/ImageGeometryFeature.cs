namespace Echo.ImageProjection;

public sealed record ImageGeometryFeature(string Id, string Geometry, IReadOnlyList<double[]> Pixels);
