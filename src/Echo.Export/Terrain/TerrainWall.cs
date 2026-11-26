using Echo.Geometry;

namespace Echo.Export.Terrain;

internal sealed record TerrainWall(string Name, V3[] Positions, uint[] Indices, float[] Uvs, double[][] GridPositions,
    int[] Boundary, TerrainGridWindow Window, byte[] Texture, double SeamRms, double SeamMax);
