namespace Echo.ImageProjection;

public sealed record ImageGeometryDocument(
    IReadOnlyList<ImageGeometryFeature> Features,
    IReadOnlyList<ImageGeometryControl>? ControlPoints = null);
