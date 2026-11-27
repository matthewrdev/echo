namespace Echo.Export.Terrain;

public sealed record TerrainRasterLayers(IReadOnlyList<string> PhotoOverlays, string? Footprint, string? Attribution);
