using Echo.Models;

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

    // Conservative box/frustum test. Borderline or non-finite bounds stay in
    // the exact projection path; large world origins include roundoff slack.
    public bool MaySee(V3 minimum, V3 maximum)
    {
        var midpoint = minimum * .5 + maximum * .5;
        var extent = maximum * .5 - minimum * .5;
        var local = midpoint - centre;
        ReadOnlySpan<V3> planes =
        [
            new(inverse[6], inverse[7], inverse[8]),
            new(focal * inverse[0] + (halfWidth - 1) * inverse[6], focal * inverse[1] + (halfWidth - 1) * inverse[7], focal * inverse[2] + (halfWidth - 1) * inverse[8]),
            new(-focal * inverse[0] + (halfWidth - 1) * inverse[6], -focal * inverse[1] + (halfWidth - 1) * inverse[7], -focal * inverse[2] + (halfWidth - 1) * inverse[8]),
            new(focal * inverse[3] + (halfHeight - 1) * inverse[6], focal * inverse[4] + (halfHeight - 1) * inverse[7], focal * inverse[5] + (halfHeight - 1) * inverse[8]),
            new(-focal * inverse[3] + (halfHeight - 1) * inverse[6], -focal * inverse[4] + (halfHeight - 1) * inverse[7], -focal * inverse[5] + (halfHeight - 1) * inverse[8])
        ];
        foreach (var plane in planes)
        {
            var bound = plane.Dot(local) + Math.Abs(plane.X) * extent.X + Math.Abs(plane.Y) * extent.Y + Math.Abs(plane.Z) * extent.Z;
            var magnitude = Math.Abs(plane.X) * (Math.Abs(midpoint.X) + Math.Abs(centre.X) + extent.X)
                + Math.Abs(plane.Y) * (Math.Abs(midpoint.Y) + Math.Abs(centre.Y) + extent.Y)
                + Math.Abs(plane.Z) * (Math.Abs(midpoint.Z) + Math.Abs(centre.Z) + extent.Z);
            if (bound < -Math.Max(1e-8, magnitude * 1e-12))
            {
                return false;
            }
        }
        return true;
    }

    public V3? Project(V3 world)
    {
        var point = CameraGeometry.Rotate(inverse, world - centre);
        return point.Z > 1e-8 ? new V3(focal * point.X / point.Z + halfWidth, focal * point.Y / point.Z + halfHeight, point.Z) : null;
    }
}
