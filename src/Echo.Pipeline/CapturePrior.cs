using Echo.Models;
using Echo.Geometry;

namespace Echo.Pipeline;

public sealed class CapturePrior
{
    public CapturePrior(GeoPosition? position, Attitude? attitude, V3 centre, CaptureFrustum? frustum)
    {
        Position = position;
        Attitude = attitude;
        Centre = centre;
        Frustum = frustum;
    }

    public GeoPosition? Position
    {
        get;
    }
    public Attitude? Attitude
    {
        get;
    }
    public V3 Centre
    {
        get;
    }
    public CaptureFrustum? Frustum
    {
        get;
    }
}
