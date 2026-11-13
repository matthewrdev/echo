namespace Echo.Pipeline;

public readonly record struct CapturePairCandidate(int ImageIndex, string ImageId, double DistanceSquared, double OverlapScore, bool DifferentElevationOrTilt);
