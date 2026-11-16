using Echo.Contracts;

namespace Echo.Pipeline;

internal sealed class GeoreferenceReport
{
    public required int SchemaVersion
    {
        get; init;
    }
    public required string Frame
    {
        get; init;
    }
    public required GeoPosition? Origin
    {
        get; init;
    }
    public required bool Referenced
    {
        get; init;
    }
    public required double[]? LocalToEcefRowMajor
    {
        get; init;
    }
    public required string AxisConvention
    {
        get; init;
    }
    public required string GeographicDatum
    {
        get; init;
    }
    public required string VerticalDatum
    {
        get; init;
    }
    public required string Formula
    {
        get; init;
    }
    public required List<CameraPose> Cameras
    {
        get; init;
    }
    public required List<string> Warnings
    {
        get; init;
    }
}
