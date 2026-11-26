namespace Echo.Export.Terrain;

public interface ITerrainStitchWriter
{
    void Write(string elevation, string imagery, string altitudeDatum, IReadOnlyList<TerrainWallInput> walls,
        string output, string work, int gridLongEdge, int textureSize, double maxSeamError, CancellationToken cancellation);
}
