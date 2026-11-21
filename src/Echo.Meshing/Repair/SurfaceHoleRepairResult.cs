namespace Echo.Meshing.Repair;

public sealed record SurfaceHoleRepairResult(
    int RepairedHoles,
    int AddedFaces);
