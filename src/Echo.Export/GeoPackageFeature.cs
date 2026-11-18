using Echo.Contracts;

namespace Echo.Export;

public sealed class GeoPackageFeature
{
    public GeoPosition[] Coordinates { get; set; } = [];
    public string SourceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public double[]? Local
    {
        get; set;
    }
    public int[]? Vertices
    {
        get; set;
    }
    public string Evidence { get; set; } = "";
}
