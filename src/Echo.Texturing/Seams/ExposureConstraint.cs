using Echo.Geometry;

namespace Echo.Texturing.Seams;

internal readonly record struct ExposureConstraint(
    V3 Difference,
    int Count);
