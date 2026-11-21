using Echo.Geometry;

namespace Echo.Simplification;

internal readonly record struct Candidate(
    int A,
    int B,
    int RevisionA,
    int RevisionB,
    V3 Position,
    double Error);
