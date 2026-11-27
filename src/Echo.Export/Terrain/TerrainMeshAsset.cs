using Echo.Geometry;

namespace Echo.Export.Terrain;

internal sealed record TerrainMeshAsset(IReadOnlyList<V3> Positions, IReadOnlyList<double[]> TerrainUvs,
    IReadOnlyList<double[]> WallUvs, IReadOnlyList<TerrainMaterial> Materials)
{
    public TerrainMeshAsset Compact()
    {
        var used = Materials.SelectMany(material => material.Triangles).Distinct().Order().ToArray();
        var indices = new int[Positions.Count];
        for (var index = 0; index < used.Length; index++)
        {
            indices[used[index]] = index;
        }

        var materials = Materials.Select(material => material with
        {
            Triangles = material.Triangles.Select(vertex => indices[vertex]).ToArray()
        }).ToArray();
        return new TerrainMeshAsset(used.Select(vertex => Positions[vertex]).ToArray(),
            used.Select(vertex => TerrainUvs[vertex]).ToArray(), used.Select(vertex => WallUvs[vertex]).ToArray(), materials);
    }
}
