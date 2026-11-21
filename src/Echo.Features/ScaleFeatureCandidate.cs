using Echo.Models;

namespace Echo.Features;

public sealed record ScaleFeatureCandidate(
    Feature Feature,
    double Response,
    int Order);
