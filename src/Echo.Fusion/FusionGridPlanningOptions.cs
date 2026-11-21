namespace Echo.Fusion;

internal readonly record struct FusionGridPlanningOptions(
    int Workers,
    int BatchCapacity,
    GridAxis Axis = GridAxis.Z);
