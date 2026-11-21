namespace Echo.Pipeline.Features;

internal sealed record FeatureImageCheckpoint(string Artifact, int PointCount, int LineCount, bool Reused);
