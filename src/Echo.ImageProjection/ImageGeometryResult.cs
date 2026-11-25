namespace Echo.ImageProjection;

public sealed record ImageGeometryResult(
    string Format, int Version, string RunId, string CoordinateFrame, string MeshArtifact,
    string? ModelSha256, string SourceImage, int ImageWidth, int ImageHeight,
    ImageCameraFit Camera, IReadOnlyList<ProjectedImageFeature> Features, IReadOnlyList<ImageProjectionReview> ReviewImages);
