using Echo.Models;
using SkiaSharp;

namespace Echo.Rendering.Previews;

public static class FusionSlicePreview
{
    public static int Draw(SKCanvas canvas, SKRect area, FusedVolume volume, string axis, int? requested)
    {
        var coordinate = requested ?? DensestSlice(volume, axis);
        var bounds = new SKRect(float.PositiveInfinity, float.PositiveInfinity, float.NegativeInfinity, float.NegativeInfinity);
        var seen = false;
        foreach (var sample in volume.Samples)
        {
            if (!float.IsFinite(sample.Distance) || Coordinate(sample, axis) != coordinate)
            {
                continue;
            }

            var point = Project(sample, axis);
            bounds.Left = Math.Min(bounds.Left, point.X);
            bounds.Top = Math.Min(bounds.Top, point.Y);
            bounds.Right = Math.Max(bounds.Right, point.X + 1);
            bounds.Bottom = Math.Max(bounds.Bottom, point.Y + 1);
            seen = true;
        }

        if (!seen)
        {
            throw new EchoException($"The saved fusion volume has no samples at {axis}={coordinate}.", 2);
        }

        var image = PreviewImage.Fit(bounds.Width, bounds.Height, area);
        var scale = image.Width / bounds.Width;
        using var paint = new SKPaint();
        foreach (var sample in volume.Samples)
        {
            if (!float.IsFinite(sample.Distance) || Coordinate(sample, axis) != coordinate)
            {
                continue;
            }

            var point = Project(sample, axis);
            var x = image.Left + (point.X - bounds.Left) * scale;
            var y = image.Top + (point.Y - bounds.Top) * scale;
            var strength = (float)Math.Clamp(Math.Abs(sample.Distance) / Math.Max(volume.VoxelSize * 3, 1e-9), 0, 1);
            var end = sample.Distance < 0 ? new SKColor(61, 117, 238) : new SKColor(245, 103, 72);
            paint.Color = PreviewImage.Mix(new SKColor(243, 237, 199), end, strength);
            canvas.DrawRect(x, y, Math.Max(scale, 1), Math.Max(scale, 1), paint);
        }

        return coordinate;
    }

    private static int DensestSlice(FusedVolume volume, string axis)
    {
        var counts = new Dictionary<int, int>();
        foreach (var sample in volume.Samples)
        {
            if (float.IsFinite(sample.Distance))
            {
                var coordinate = Coordinate(sample, axis);
                counts[coordinate] = counts.GetValueOrDefault(coordinate) + 1;
            }
        }

        if (counts.Count == 0)
        {
            throw new EchoException("The saved fusion volume has no samples to preview.", 2);
        }

        return counts.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key).First().Key;
    }

    private static int Coordinate(VolumeSample sample, string axis) => axis switch
    {
        "x" => sample.X,
        "y" => sample.Y,
        _ => sample.Z
    };

    private static SKPoint Project(VolumeSample sample, string axis) => axis switch
    {
        "x" => new SKPoint(-sample.Z, -sample.Y),
        "y" => new SKPoint(sample.X, sample.Z),
        _ => new SKPoint(sample.X, -sample.Y)
    };
}
