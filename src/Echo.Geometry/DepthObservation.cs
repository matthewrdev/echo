namespace Echo.Geometry;

public readonly struct DepthObservation
{
    public DepthObservation(double distance, double cameraDepth, bool hasFullCoverage)
    {
        Distance = distance;
        CameraDepth = cameraDepth;
        HasFullCoverage = hasFullCoverage;
    }

    public double Distance
    {
        get;
    }
    public double CameraDepth
    {
        get;
    }
    public bool HasFullCoverage
    {
        get;
    }
    public bool IsValid => CameraDepth > 0 && double.IsFinite(Distance);
}
