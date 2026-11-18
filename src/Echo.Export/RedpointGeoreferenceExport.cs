namespace Echo.Export;

public sealed class RedpointGeoreferenceExport
{
    public int SchemaVersion { get; set; } = 1;
    public string Format { get; set; } = "echo-redpoint-georeference";
    public string ModelFile { get; set; } = "cliff.glb";
    public string ModelSha256 { get; set; } = "";
    public string CoordinateSpace { get; set; } = "model-local";
    public string AxisConvention { get; set; } = "X east, Y up, Z south; metres; right-handed";
    public string VerticalDatum { get; set; } = "";
    public string Provenance { get; set; } = "Derived from Echo alignment; these are calibration samples, not surveyed control points.";
    public RedpointGeoReference GeoReference { get; set; } = new();
}
