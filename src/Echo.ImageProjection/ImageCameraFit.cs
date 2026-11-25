using Echo.Models;

namespace Echo.ImageProjection;

public sealed record ImageCameraFit(
    string Method, CameraPose Pose, int? Inliers = null, int? CandidateCount = null,
    double? MedianErrorPixels = null, double? MinimumDepth = null,
    int? HeldOutInliers = null, int? HeldOutCandidates = null,
    double? HeldOutMedianErrorPixels = null, double? InlierThresholdPixels = null,
    int? FirstHitSupport = null, int? SelectedHitSupport = null);
