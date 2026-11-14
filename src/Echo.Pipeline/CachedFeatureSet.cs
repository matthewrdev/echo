using Echo.Contracts;

namespace Echo.Pipeline;

internal sealed record CachedFeatureSet(string Path, FeatureSet Features, long EstimatedBytes);
