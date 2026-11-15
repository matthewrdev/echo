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
        var needsCompletion = mesh.SourceImages.Any(string.IsNullOrEmpty);
        var welded = needsCompletion ? SurfaceTopologyBuilder.Build(mesh, cancellation).WeldedVertices : [];
        var vertexCount = welded.Length > 0 ? welded.Max() + 1 : 0;
        var colourSums = new V3[vertexCount];
        var colourWeights = new int[vertexCount];
        long painted = 0;
        Span<V3> projected = stackalloc V3[3];
        Span<V3> corrections = stackalloc V3[3];
        Span<TextureCoordinate> uv = stackalloc TextureCoordinate[3];
        foreach (var group in Enumerable.Range(0, mesh.Faces.Count).GroupBy(i => mesh.SourceImages[i]))
        {
            if (string.IsNullOrEmpty(group.Key))
            {
                continue;
            }
            var photo = photos[group.Key];
            var camera = cameras[group.Key];
            var projection = new CameraProjector(photo, camera);
            using var source = SKBitmap.Decode(store.PathFor(photo.Source));
            var pixels = images.BorrowPixels(source, out var swapRedBlue);
            var skyGuard = project.Settings.TextureSuppressSky
                ? TextureSkyGuardBuilder.Build(pixels, source.Width, source.Height, swapRedBlue, project.Settings.TextureSkyMarginPixels, cancellation)
                : null;
            foreach (var index in group)
            {
                cancellation.ThrowIfCancellationRequested();
                var face = mesh.Faces[index];
                for (var corner = 0; corner < 3; corner++)
                {
                    projected[corner] = projection.Project(new V3(mesh.Vertices[face[corner]]))
                        ?? throw new EchoException("Texture source lies behind its assigned camera.");
                    var offsets = texture.Corrections?.CornerOffsets;
                    corrections[corner] = offsets is { Length: > 0 } ? offsets[index * 3 + corner] : default;
                    var coordinate = texture.Uvs[face[corner]];
                    uv[corner] = new TextureCoordinate(coordinate[0], coordinate[1]);
                    if (needsCompletion)
                    {
                        var pixel = TextureSampling.Bilinear(pixels, source.Width, source.Height, projected[corner].X, projected[corner].Y, skyGuard);
                        var colour = new V3(swapRedBlue ? pixel.Blue : pixel.Red, pixel.Green, swapRedBlue ? pixel.Red : pixel.Blue) + corrections[corner];
                        colour = new V3(Math.Clamp(colour.X, 0, 255), Math.Clamp(colour.Y, 0, 255), Math.Clamp(colour.Z, 0, 255));
                        var vertex = welded[face[corner]];
                        colourSums[vertex] += colour;
                        colourWeights[vertex]++;
                    }
                }
                painted += PaintTriangle(target, size, pixels, source.Width, source.Height, uv, projected, corrections, swapRedBlue, skyGuard: skyGuard);
            }
            GC.KeepAlive(source);
        }

        if (needsCompletion)
        {
            var colours = TextureColourCompletion.Extend(mesh, welded, colourSums, colourWeights, cancellation);
            Span<V3> fill = stackalloc V3[3];
            for (var index = 0; index < mesh.Faces.Count; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!string.IsNullOrEmpty(mesh.SourceImages[index]))
                {
                    continue;
                }
                var face = mesh.Faces[index];
                for (var corner = 0; corner < 3; corner++)
                {
                    var coordinate = texture.Uvs[face[corner]];
                    uv[corner] = new TextureCoordinate(coordinate[0], coordinate[1]);
                    fill[corner] = colours[welded[face[corner]]];
                }
                painted += PaintTriangle(target, size, [], 0, 0, uv, [], [], false, fill);
            }
        }

        // Alpha temporarily records subpixel coverage, never opacity. Make every
        // covered texel opaque before extending colours outside UV island edges.
        for (var index = 0; index < target.Length; index++)
        {
            if (target[index].Alpha != 0)
            {
                target[index] = target[index].WithAlpha(255);
            }
        }
        TexturePadding.Extend(target, size, texture.GutterPixels > 0 ? texture.GutterPixels : project.Settings.TextureGutter, cancellation);
        TextureBackground.Fill(target, size, cancellation);

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

    private long PaintTriangle(Span<SKColor> target, int size, ReadOnlySpan<SKColor> source, int width, int height,
        ReadOnlySpan<TextureCoordinate> uv, ReadOnlySpan<V3> projected, ReadOnlySpan<V3> corrections,
        bool swapRedBlue, ReadOnlySpan<V3> fill = default, TextureSkyGuard? skyGuard = null)
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
                var red = 0;
                var green = 0;
                var blue = 0;
                var coverage = 0;
                for (var sampleY = 0; sampleY < 2; sampleY++)
                {
                    for (var sampleX = 0; sampleX < 2; sampleX++)
                    {
                        var px = x + .25 + sampleX * .5;
                        var py = y + .25 + sampleY * .5;
                        var a = ((y1 - y2) * (px - x2) + (x2 - x1) * (py - y2)) / determinant;
                        var b = ((y2 - y0) * (px - x2) + (x0 - x2) * (py - y2)) / determinant;
                        var c = 1 - a - b;
                        var outside = a < -1e-7 || b < -1e-7 || c < -1e-7;
                        if (outside)
                        {
                            continue;
                        }

                        if (fill.Length == 3)
                        {
                            var colour = fill[0] * a + fill[1] * b + fill[2] * c;
                            red += (int)Math.Clamp(colour.X, 0, 255);
                            green += (int)Math.Clamp(colour.Y, 0, 255);
                            blue += (int)Math.Clamp(colour.Z, 0, 255);
                        }
                        else
                        {
                            var depth = a * projected[0].Z + b * projected[1].Z + c * projected[2].Z;
                            var sx = (a * projected[0].X * projected[0].Z + b * projected[1].X * projected[1].Z + c * projected[2].X * projected[2].Z) / depth;
                            var sy = (a * projected[0].Y * projected[0].Z + b * projected[1].Y * projected[1].Z + c * projected[2].Y * projected[2].Z) / depth;
                            var colour = TextureSampling.Bilinear(source, width, height, sx, sy, skyGuard);
                            var correction = corrections[0] * a + corrections[1] * b + corrections[2] * c;
                            red += (int)Math.Clamp((swapRedBlue ? colour.Blue : colour.Red) + correction.X, 0, 255);
                            green += (int)Math.Clamp(colour.Green + correction.Y, 0, 255);
                            blue += (int)Math.Clamp((swapRedBlue ? colour.Red : colour.Blue) + correction.Z, 0, 255);
                        }
                        coverage++;
                    }
                }
                if (coverage == 0)
                {
                    continue;
                }

                var index = y * size + x;
                var previous = target[index];
                if (previous.Alpha == 0)
                {
                    painted++;
                }
                var total = previous.Alpha + coverage;
                target[index] = new SKColor((byte)((red + previous.Red * previous.Alpha) / total),
                    (byte)((green + previous.Green * previous.Alpha) / total),
                    (byte)((blue + previous.Blue * previous.Alpha) / total), (byte)Math.Min(255, total));
            }
        }

        return painted;
    }

}
