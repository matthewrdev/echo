using System.Numerics;
using Echo.Models;
using SkiaSharp;

namespace Echo.Rendering.Previews;

internal sealed class CloudPreviewFrame
{
    private readonly Vector3 forward;
    private readonly Vector3 right;
    private readonly Vector3 up;
    private Vector2 minimum = new(float.PositiveInfinity);
    private Vector2 maximum = new(float.NegativeInfinity);
    private Vector2 offset;
    private float scale;

    public CloudPreviewFrame(string view)
    {
        forward = view switch
        {
            "front" => Vector3.UnitZ,
            "back" => -Vector3.UnitZ,
            "left" => -Vector3.UnitX,
            "right" => Vector3.UnitX,
            "top" => Vector3.UnitY,
            "oblique" => Vector3.Normalize(new Vector3(0.3f, 0.18f, 0.96f)),
            _ => throw new EchoException("Unknown preview view.", 2)
        };
        right = Vector3.Normalize(Vector3.Cross(view == "top" ? -Vector3.UnitZ : Vector3.UnitY, forward));
        up = Vector3.Cross(forward, right);
    }

    public float FrustumLength => 10 / scale;

    public void Include(double[] position)
    {
        if (TryPosition(position, out var vector))
        {
            var projected = Coordinates(vector);
            minimum = Vector2.Min(minimum, projected);
            maximum = Vector2.Max(maximum, projected);
        }
    }

    public void Fit(int width, int height)
    {
        if (!float.IsFinite(minimum.X))
        {
            throw new EchoException("The saved artifact has no points or camera poses to preview.", 2);
        }

        var size = maximum - minimum;
        var bounds = PreviewImage.Fit(Math.Max(size.X, 0.0001f), Math.Max(size.Y, 0.0001f),
            new SKRect(width * 0.055f, height * 0.055f, width * 0.945f, height * 0.945f));
        scale = size.X >= size.Y
            ? bounds.Width / Math.Max(size.X, 0.0001f)
            : bounds.Height / Math.Max(size.Y, 0.0001f);
        var centre = minimum + size / 2;
        offset = new Vector2(bounds.MidX, bounds.MidY) - centre * scale;
    }

    public SKPoint Project(Vector3 position)
    {
        var pixel = Coordinates(position) * scale + offset;
        return new SKPoint(pixel.X, pixel.Y);
    }

    public bool TryProject(double[] position, out SKPoint pixel, out float depth)
    {
        pixel = default;
        depth = 0;
        if (!TryPosition(position, out var vector))
        {
            return false;
        }

        pixel = Project(vector);
        depth = Vector3.Dot(vector, forward);
        return true;
    }

    private Vector2 Coordinates(Vector3 position) => new(Vector3.Dot(position, right), -Vector3.Dot(position, up));

    public bool TryPosition(double[] position, out Vector3 vector)
    {
        vector = position.Length >= 3
            ? new Vector3((float)position[0], (float)position[1], (float)position[2])
            : new Vector3(float.NaN);
        return float.IsFinite(vector.X) && float.IsFinite(vector.Y) && float.IsFinite(vector.Z);
    }
}
