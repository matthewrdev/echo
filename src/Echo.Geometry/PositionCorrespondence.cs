namespace Echo.Geometry;

public readonly record struct PositionCorrespondence(
    V3 Source,
    V3 Target,
    double Tolerance);
