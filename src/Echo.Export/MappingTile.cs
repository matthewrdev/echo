namespace Echo.Export;

internal sealed record MappingTile(string Directory, string Crs, MappingGrid Grid)
{
    public string PathFor(string filename) => Path.Combine(Directory, filename);
}
