namespace Echo.ImageProjection;

public sealed record ImageGeometryRequest(
    string ProjectPath, string RunId, string? ImagePath, string? ImageId,
    ImageGeometryDocument Document, double SampleStep, double? MaximumJump,
    IReadOnlyList<string> ReferenceImageIds, double? FocalPixels, IReadOnlyList<string> ReviewImageIds);
