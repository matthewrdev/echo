namespace Echo.ImageProjection;

public sealed record ImageProjectionReviewFeature(string Id, IReadOnlyList<ImageProjectionReviewSample?> Samples);
