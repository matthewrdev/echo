using Echo.Models;
using Echo.Geometry;

namespace Echo.Meshing;

internal static class FaceViewResolution
{
    public static double Density(DepthView view, Photo photo, V3 a, V3 b, V3 c)
        => Density(view, photo, a, b, c, (b - a).Cross(c - a).Length);

    public static double Density(DepthView view, Photo photo, V3 a, V3 b, V3 c, double surfaceArea)
    {
        var first = view.Projection.Project(a);
        var second = view.Projection.Project(b);
        var third = view.Projection.Project(c);
        var covered = Inside(first, photo) && Inside(second, photo) && Inside(third, photo);
        if (!covered)
        {
            return 0;
        }

        if (!(surfaceArea > 0))
        {
            return 0;
        }
        var u = second!.Value - first!.Value;
        var v = third!.Value - first.Value;
        // Both areas omit the same factor of one half. Original-photo pixels
        // account for proximity, focal length and foreshortening independently
        // of the resolution at which a supporting depth map was computed.
        var density = Math.Abs(u.X * v.Y - u.Y * v.X) / surfaceArea;
        return double.IsFinite(density) ? density : 0;
    }

    private static bool Inside(V3? pixel, Photo photo)
    {
        if (!pixel.HasValue)
        {
            return false;
        }
        var insideX = pixel.Value.X >= 1 && pixel.Value.X < photo.Width - 1;
        var insideY = pixel.Value.Y >= 1 && pixel.Value.Y < photo.Height - 1;
        return insideX && insideY;
    }
}
