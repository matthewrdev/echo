using Echo.Models;
using Echo.Geometry;

namespace Echo.Reconstruction;

internal sealed class RotationObservation
{
    public CameraPose Other { get; set; } = new();
    public V3 OwnRay
    {
        get; set;
    }
    public V3 OtherRay
    {
        get; set;
    }
}
