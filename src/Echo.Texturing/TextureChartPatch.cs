namespace Echo.Texturing;

internal static class TextureChartPatch
{
    public static TexturePatch Create(TextureChartGeometry geometry, TextureChartMesh chart,
        IReadOnlyList<int> faces, double[] coordinates)
    {
        var patch = new TexturePatch();
        foreach (var face in faces)
        {
            patch.Faces.Add(face);
            patch.Triangles.Add(geometry.Surface.Faces[face]);
            foreach (var vertex in geometry.Surface.Faces[face])
            {
                var local = chart.LocalVertices[geometry.WeldedVertices[vertex]];
                patch.Coordinates[vertex] = [coordinates[local * 2], coordinates[local * 2 + 1]];
            }
        }
        return patch;
    }
}
