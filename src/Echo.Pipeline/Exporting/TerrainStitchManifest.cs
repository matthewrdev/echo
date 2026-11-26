namespace Echo.Pipeline.Exporting;

public sealed record TerrainStitchManifest(string Elevation, string Imagery, string VerticalDatum,
    IReadOnlyList<TerrainStitchWallRun> Walls, int GridLongEdge = 512, int TerrainTextureSize = 8192, double MaxSeamErrorMetres = 5);
