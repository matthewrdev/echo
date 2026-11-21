using Echo.Models;

namespace Echo.Pipeline.Features;

internal sealed record CachedFeatureSet(
    string Path,
    FeatureSet Features,
    long EstimatedBytes);
