using Echo.Models;

namespace Echo.ImageProjection;

internal sealed record EstimatedCamera(CameraPose Pose, IReadOnlyList<RegistrationMatch> Inliers, double MedianError);
