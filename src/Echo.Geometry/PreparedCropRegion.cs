using Echo.Models;

namespace Echo.Geometry;

// Precompute the inverse rotation once, rather than evaluating trigonometry
// for every depth sample, fusion voxel and mesh vertex.
public sealed class PreparedCropRegion
{
    private readonly ExclusionRegion region;
    private readonly V3 centre;
    private readonly double cosine;
    private readonly double sine;

    public PreparedCropRegion(ExclusionRegion region)
    {
        this.region = region;
        centre = new V3(region.CentreX, region.CentreY, region.CentreZ);
        // Only boxes rotate, around the vertical Y axis. Cylinders stay upright.
        var angle = region.Shape == ExclusionShape.Box ? region.RotationY % 360 * Math.PI / 180 : 0;
        cosine = Math.Cos(angle);
        sine = Math.Sin(angle);
    }

    public bool Contains(V3 point) => region.Shape == ExclusionShape.ProjectedHull
        ? CropHullGeometry.Contains(region.Hull!, point)
        : ExclusionGeometry.ContainsLocal(region, ToLocal(point));

    public bool Intersects(V3 a, V3 b, V3 c) => region.Shape == ExclusionShape.ProjectedHull
        ? CropHullGeometry.Intersects(region.Hull!, a, b, c)
        : ExclusionGeometry.IntersectsLocal(region, ToLocal(a), ToLocal(b), ToLocal(c));

    private V3 ToLocal(V3 point)
    {
        var delta = point - centre;
        return new V3(delta.X * cosine - delta.Z * sine, delta.Y, delta.X * sine + delta.Z * cosine);
    }
}
