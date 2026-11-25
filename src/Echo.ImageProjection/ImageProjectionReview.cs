namespace Echo.ImageProjection;

public sealed record ImageProjectionReview(
    string ImageId, int Width, int Height, IReadOnlyList<ImageProjectionReviewFeature> Features);
