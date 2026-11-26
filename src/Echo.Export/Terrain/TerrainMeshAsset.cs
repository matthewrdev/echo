using Echo.Geometry;

namespace Echo.Export.Terrain;

internal sealed record TerrainMeshAsset(IReadOnlyList<V3> Positions, IReadOnlyList<double[]> TerrainUvs,
    IReadOnlyList<double[]> WallUvs, IReadOnlyList<TerrainMaterial> Materials);
