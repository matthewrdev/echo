using Echo.Contracts;

namespace Echo.Geometry;

public sealed class CameraProjector
{
    private readonly double[] inverse;
    private readonly V3 centre;
    private readonly double focal;
    private readonly double halfWidth;
    private readonly double halfHeight;

    public CameraProjector(Photo photo, CameraPose camera)
    {
        inverse = CameraGeometry.Transpose(camera.Rotation);
        centre = new V3(camera.Centre);
        focal = CameraGeometry.Focal(photo, camera);
        halfWidth = photo.Width * .5;
        halfHeight = photo.Height * .5;
    }

    public V3? Project(V3 world)
    {
        var point = CameraGeometry.Rotate(inverse, world - centre);
        return point.Z > 1e-8 ? new V3(focal * point.X / point.Z + halfWidth, focal * point.Y / point.Z + halfHeight, point.Z) : null;
    }
}
