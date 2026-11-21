using Echo.Models;
using Echo.Geometry;

namespace Echo.Depth;

internal sealed class DepthNeighbourCandidate
{
    public DepthNeighbourCandidate(CameraPose camera, double distance, byte[] quality)
    {
        Camera = camera;
        Centre = new V3(camera.Centre);
        Distance = distance;
        Quality = quality;
    }

    public CameraPose Camera
    {
        get;
    }
    public V3 Centre
    {
        get;
    }
    public double Distance
    {
        get;
    }
    public byte[] Quality
    {
        get;
    }
}
