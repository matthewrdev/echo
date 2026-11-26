namespace Echo.Export.Terrain;

internal sealed record TerrainMaterial(string Name, int[] Triangles, byte[] Image, int TextureCoordinate);
