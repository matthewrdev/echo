namespace Echo.Optimization;

internal readonly record struct ObservationKey(
    int Camera,
    long X,
    long Y);
