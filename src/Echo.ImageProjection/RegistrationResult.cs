namespace Echo.ImageProjection;

internal sealed record RegistrationResult(ImageCameraFit Fit, IReadOnlyList<RegistrationMatch> Support);
