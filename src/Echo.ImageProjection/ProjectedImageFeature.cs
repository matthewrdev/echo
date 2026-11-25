namespace Echo.ImageProjection;

public sealed record ProjectedImageFeature(
    string Id, string Geometry, IReadOnlyList<ImageGeometrySample> Samples, IReadOnlyList<string> ReviewFlags);
