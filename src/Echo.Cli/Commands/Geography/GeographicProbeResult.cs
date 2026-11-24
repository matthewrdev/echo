using Echo.Models;

namespace Echo.Cli.Commands.Geography;

internal sealed record GeographicProbeResult(GeoPosition Coordinate, double[] Local, double Accuracy, int SurfaceIntersections,
    string Placement)
{
    public int SchemaVersion => 1;
    public string? RunId
    {
        get; init;
    }
}
