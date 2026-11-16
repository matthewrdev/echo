using Echo.Contracts;

namespace Echo.Export;

public sealed class GisReference
{
    public int SchemaVersion { get; set; } = 1;
    public string HorizontalCrs { get; set; } = "OGC:CRS84 (WGS84 longitude, latitude; QGIS EPSG:4326)";
    public string AxisConvention { get; set; } = "Local X east, Y up, Z south, metres";
    public string Frame { get; set; } = "";
    public GeoPosition Origin { get; set; } = new();
    public double[] LocalToEcefRowMajor { get; set; } = [];
    public bool GeometryHasEllipsoidalHeight
    {
        get; set;
    }
    public string VerticalNote { get; set; } = "";
    public List<string> Warnings { get; set; } = [];
}
