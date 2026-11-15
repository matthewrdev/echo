using Echo.Geometry;

namespace Echo.Simplification;

internal sealed record Candidate(int A, int B, int RevisionA, int RevisionB, V3 Position, double Error);
