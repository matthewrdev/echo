using System.Numerics;
using Echo.Models;
using SkiaSharp;

namespace Echo.Rendering.Previews;

public static class CloudPreview
{
    public static void Draw(SKCanvas canvas, int width, int height, IList<CloudPoint> points,
        IReadOnlyList<CameraPose> cameras, string view)
    {
        var frame = new CloudPreviewFrame(view);
        foreach (var point in points)
        {
            frame.Include(point.Position);
        }

        foreach (var camera in cameras)
        {
            frame.Include(camera.Centre);
        }

        frame.Fit(width, height);
        DrawPoints(canvas, width, height, points, frame.TryProject);
        using var paint = new SKPaint { IsAntialias = true, StrokeWidth = 1.3f, Color = new SKColor(255, 185, 81, 215) };
        foreach (var camera in cameras)
        {
            DrawCamera(canvas, frame, camera, paint);
        }
    }

    public static int DrawFromCamera(SKCanvas canvas, int width, int height, IList<CloudPoint> points, ModelCamera camera)
    {
        var projection = new PointPreviewCamera(camera, width, height);
        return DrawPoints(canvas, width, height, points, projection.TryProject);
    }

    private static int DrawPoints(SKCanvas canvas, int width, int height, IList<CloudPoint> points,
        PointPreviewProjection project)
    {
        using var image = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        image.Erase(PreviewImage.Background());
        var pixels = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, SKColor>(image.GetPixelSpan());
        var nearest = new float[checked(width * height)];
        Array.Fill(nearest, float.NegativeInfinity);
        var visible = 0;
        foreach (var point in points)
        {
            if (!project(point.Position, out var projected, out var depth))
            {
                continue;
            }

            visible++;
            var colour = point.Colour is { Length: >= 3 } rgb ? new SKColor(rgb[0], rgb[1], rgb[2]) : new SKColor(195, 215, 235);
            var x = (int)MathF.Round(projected.X);
            var y = (int)MathF.Round(projected.Y);
            for (var row = Math.Max(y - 1, 0); row <= Math.Min(y, height - 1); row++)
            {
                for (var column = Math.Max(x - 1, 0); column <= Math.Min(x, width - 1); column++)
                {
                    var index = row * width + column;
                    if (depth > nearest[index])
                    {
                        nearest[index] = depth;
                        pixels[index] = colour;
                    }
                }
            }
        }

        canvas.DrawBitmap(image, 0, 0);
        return visible;
    }

    private static void DrawCamera(SKCanvas canvas, CloudPreviewFrame frame, CameraPose camera, SKPaint paint)
    {
        var rotation = camera.Rotation;
        var validRotation = rotation.Length == 9 && rotation.All(double.IsFinite);
        if (!validRotation || !frame.TryPosition(camera.Centre, out var origin))
        {
            return;
        }

        var length = frame.FrustumLength;
        var centre = origin + new Vector3((float)rotation[2], (float)rotation[5], (float)rotation[8]) * length;
        var right = new Vector3((float)rotation[0], (float)rotation[3], (float)rotation[6]) * length * 0.55f;
        var down = new Vector3((float)rotation[1], (float)rotation[4], (float)rotation[7]) * length * 0.38f;
        var corners = new[]
        {
            frame.Project(centre - right - down), frame.Project(centre + right - down),
            frame.Project(centre + right + down), frame.Project(centre - right + down)
        };
        var start = frame.Project(origin);
        for (var index = 0; index < corners.Length; index++)
        {
            canvas.DrawLine(start, corners[index], paint);
            canvas.DrawLine(corners[index], corners[(index + 1) % corners.Length], paint);
        }
    }
}
