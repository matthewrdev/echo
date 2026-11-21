using Echo.Models;

namespace Echo.Pipeline;

internal sealed record CachedFeatureSet(string Path, FeatureSet Features, long EstimatedBytes);
