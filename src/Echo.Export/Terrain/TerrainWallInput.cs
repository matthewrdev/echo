namespace Echo.Export.Terrain;

public sealed record TerrainWallInput(string Directory, double? HeightOffsetMetres, string? Name = null);
