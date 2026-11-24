using SkiaSharp;

namespace Echo.Rendering.Previews;

internal sealed class PointPreviewCamera
{
    private readonly ModelCamera camera;
    private readonly SKRect frame;
    private readonly double focal;

    public PointPreviewCamera(ModelCamera camera, int width, int height)
    {
        camera.Validate();
        this.camera = camera;
        frame = PreviewImage.Fit(camera.PhotoWidth, camera.PhotoHeight, new SKRect(0, 0, width, height));
        focal = camera.FocalPixels * frame.Width / camera.PhotoWidth;
    }

    public bool TryProject(double[] point, out SKPoint pixel, out float depth)
    {
        pixel = default;
        depth = 0;
        if (point.Length != 3)
        {
            return false;
        }

        // R is camera-to-world. Its transpose maps the world-space offset into the camera.
        var r = camera.Rotation;
        var x = point[0] - camera.Centre[0];
        var y = point[1] - camera.Centre[1];
        var z = point[2] - camera.Centre[2];
        var forward = r[2] * x + r[5] * y + r[8] * z;
        if (!(forward > 1e-8))
        {
            return false;
        }

        var u = frame.MidX + focal * (r[0] * x + r[3] * y + r[6] * z) / forward;
        var v = frame.MidY + focal * (r[1] * x + r[4] * y + r[7] * z) / forward;
        var inside = u >= frame.Left && u < frame.Right && v >= frame.Top && v < frame.Bottom;
        if (!inside)
        {
            return false;
        }

        pixel = new SKPoint((float)u, (float)v);
        depth = -(float)forward;
        return true;
    }
}
