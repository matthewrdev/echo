using Echo.Geometry;

namespace Echo.Reconstruction;

internal readonly record struct MatchObservationKey(
    ImageObservationKey Left,
    ImageObservationKey Right);
