using Echo.Models;

namespace Echo.Depth;

public sealed record SubjectDistancePlan
{
    public SubjectDistanceMode Mode
    {
        get; init;
    }
    public double? MaximumDistance
    {
        get; init;
    }
    public double CaptureExtent
    {
        get; init;
    }
    public double ForegroundDepth
    {
        get; init;
    }
    public int EvidenceCameras
    {
        get; init;
    }
    public string Source { get; init; } = "";
}
