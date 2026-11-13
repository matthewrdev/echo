using Echo.Contracts;

namespace Echo.Features;

public sealed record ScaleFeatureCandidate(Feature Feature, double Response, int Order);
