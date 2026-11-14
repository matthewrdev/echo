using System.ComponentModel.Composition;
using System.Runtime.InteropServices;
using Echo.Contracts;
using Echo.Geometry;
using Echo.Imaging;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Texturing;

[Export(typeof(ITextureRasterizer)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class TextureRasterizer : ITextureRasterizer
{
    private readonly IRasterImageService images;

    [ImportingConstructor]
    public TextureRasterizer(IRasterImageService images)
    {
        this.images = images;
    }

    public double Paint(IProjectStore store, ProjectData project, ReconstructionScene scene, Surface mesh,
        TextureResult texture, IReadOnlyList<TexturePatch> patches, SKBitmap atlas, CancellationToken cancellation)
    {
        var size = texture.Size;
        var byteColour = atlas.ColorType is SKColorType.Bgra8888 or SKColorType.Rgba8888;
        var directLayout = BitConverter.IsLittleEndian
            && byteColour
            && atlas.RowBytes == size * sizeof(uint);
        var swapTargetRedBlue = directLayout && atlas.ColorType == SKColorType.Rgba8888;
        if (atlas.Width != size || atlas.Height != size)
        {
            throw new EchoException("Atlas dimensions do not match the texture layout.");
        }

        SKColor[]? managedTarget = directLayout ? null : new SKColor[checked(size * size)];
        Span<SKColor> target = managedTarget != null
            ? managedTarget : MemoryMarshal.Cast<byte, SKColor>(atlas.GetPixelSpan());
        target.Clear();
        var photos = project.Images.ToDictionary(p => p.Id);
        var cameras = scene.Cameras.ToDictionary(c => c.ImageId);
        long painted = 0;
        Span<V3> projected = stackalloc V3[3];
        Span<TextureCoordinate> uv = stackalloc TextureCoordinate[3];
        foreach (var group in Enumerable.Range(0, mesh.Faces.Count).GroupBy(i => mesh.SourceImages[i]))
        {
            var photo = photos[group.Key];
            var camera = cameras[group.Key];
            var projection = new CameraProjector(photo, camera);
            using var source = SKBitmap.Decode(store.PathFor(photo.Source));
            var pixels = images.BorrowPixels(source, out var swapRedBlue);
            foreach (var index in group)
            {
                cancellation.ThrowIfCancellationRequested();
                var face = mesh.Faces[index];
                for (var corner = 0; corner < 3; corner++)
                {
                    projected[corner] = projection.Project(new V3(mesh.Vertices[face[corner]]))
                        ?? throw new EchoException("Texture source lies behind its assigned camera.");
                    var coordinate = texture.Uvs[face[corner]];
                    uv[corner] = new TextureCoordinate(coordinate[0], coordinate[1]);
                }
                painted += PaintTriangle(target, size, pixels, source.Width, source.Height, uv, projected, swapRedBlue);
            }
            GC.KeepAlive(source);
        }

        Pad(target, size, project.Settings.TextureGutter, cancellation);

        for (var i = 0; i < target.Length; i++)
        {
            var colour = target[i].Alpha == 0 ? new SKColor(48, 52, 58) : target[i];
            // Painting/padding use logical SKColor values; publish native channel order only
            // once every pixel is opaque. Skia never reads the temporary padding alpha marks.
            target[i] = swapTargetRedBlue ? new SKColor(colour.Blue, colour.Green, colour.Red) : colour;
        }

        if (managedTarget != null)
        {
            atlas.Pixels = managedTarget;
        }
        GC.KeepAlive(atlas);
        return (double)painted / (size * size);
    }

    private long PaintTriangle(Span<SKColor> target, int size, ReadOnlySpan<SKColor> source, int width, int height, ReadOnlySpan<TextureCoordinate> uv, ReadOnlySpan<V3> projected, bool swapRedBlue)
    {
        var x0 = uv[0].X * size;
        var y0 = uv[0].Y * size;
        var x1 = uv[1].X * size;
        var y1 = uv[1].Y * size;
        var x2 = uv[2].X * size;
        var y2 = uv[2].Y * size;
        var determinant = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2);
        if (Math.Abs(determinant) < 1e-10)
        {
            return 0;
        }

        var left = Math.Max(0, (int)Math.Floor(Math.Min(x0, Math.Min(x1, x2))));
        var right = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(x0, Math.Max(x1, x2))));
        var top = Math.Max(0, (int)Math.Floor(Math.Min(y0, Math.Min(y1, y2))));
        var bottom = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(y0, Math.Max(y1, y2))));
        long painted = 0;
        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                var a = ((y1 - y2) * (x + .5 - x2) + (x2 - x1) * (y + .5 - y2)) / determinant;
                var b = ((y2 - y0) * (x + .5 - x2) + (x0 - x2) * (y + .5 - y2)) / determinant;
                var c = 1 - a - b;
                var outside = a < -1e-7 || b < -1e-7 || c < -1e-7;
                if (outside)
                {
                    continue;
                }

                var depth = a * projected[0].Z + b * projected[1].Z + c * projected[2].Z;
                var sx = (a * projected[0].X * projected[0].Z + b * projected[1].X * projected[1].Z + c * projected[2].X * projected[2].Z) / depth;
                var sy = (a * projected[0].Y * projected[0].Z + b * projected[1].Y * projected[1].Z + c * projected[2].Y * projected[2].Z) / depth;
                var index = y * size + x;
                if (target[index].Alpha == 0)
                {
                    painted++;
                }

                var colour = Sample(source, width, height, sx, sy);
                target[index] = swapRedBlue ? new SKColor(colour.Blue, colour.Green, colour.Red) : colour;
            }
        }

        return painted;
    }

    private SKColor Sample(ReadOnlySpan<SKColor> source, int width, int height, double x, double y)
    {
        x = Math.Clamp(x, 0, width - 1.001);
        y = Math.Clamp(y, 0, height - 1.001);
        var ix = (int)x;
        var iy = (int)y;
        var dx = x - ix;
        var dy = y - iy;
        var a = source[iy * width + ix];
        var b = source[iy * width + ix + 1];
        var c = source[(iy + 1) * width + ix];
        var d = source[(iy + 1) * width + ix + 1];
        byte Blend(byte aa, byte bb, byte cc, byte dd) => (byte)Math.Clamp(
            aa * (1 - dx) * (1 - dy) + bb * dx * (1 - dy) + cc * (1 - dx) * dy + dd * dx * dy, 0, 255);
        return new SKColor(Blend(a.Red, b.Red, c.Red, d.Red), Blend(a.Green, b.Green, c.Green, d.Green), Blend(a.Blue, b.Blue, c.Blue, d.Blue));
    }

    private void Pad(Span<SKColor> pixels, int size, int gutter, CancellationToken cancellation)
    {
        var left = 0;
        var top = 0;
        var right = size - 1;
        var bottom = size - 1;
        var changed = new List<int>();
        for (var iteration = 0; iteration < gutter; iteration++)
        {
            cancellation.ThrowIfCancellationRequested();
            changed.Clear();
            for (var y = top; y <= bottom; y++)
            {
                for (var x = left; x <= right; x++)
                {
                    var index = y * size + x;
                    if (pixels[index].Alpha != 0)
                    {
                        continue;
                    }

                    var color = Neighbour(pixels, size, x, y, left, top, right, bottom);
                    if (color.Alpha == 255)
                    {
                        pixels[index] = color.WithAlpha(1);
                        changed.Add(index);
                    }
                }
            }

            foreach (var index in changed)
            {
                pixels[index] = pixels[index].WithAlpha(255);
            }
        }
    }

    private SKColor Neighbour(ReadOnlySpan<SKColor> pixels, int size, int x, int y, int left, int top, int right, int bottom)
    {
        for (var yy = Math.Max(top, y - 1); yy <= Math.Min(bottom, y + 1); yy++)
        {
            for (var xx = Math.Max(left, x - 1); xx <= Math.Min(right, x + 1); xx++)
            {
                var color = pixels[yy * size + xx];
                if (color.Alpha == 255)
                {
                    return color;
                }
            }
        }

        return SKColors.Transparent;
    }
}
