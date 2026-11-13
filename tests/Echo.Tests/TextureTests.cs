using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Imaging;
using Echo.Projects;
using Echo.Texturing;
using SkiaSharp;

namespace Echo.Tests;

[Export, PartCreationPolicy(CreationPolicy.Shared)]
public sealed class TextureTests
{
    private readonly ITextureBaker baker;
    private readonly IProjectStoreFactory stores;
    private readonly IRasterImageService images;
    private readonly ITexturePatchPacker packer;

    [ImportingConstructor]
    public TextureTests(ITextureBaker baker, IProjectStoreFactory stores, IRasterImageService images, ITexturePatchPacker packer)
    {
        this.baker = baker;
        this.stores = stores;
        this.images = images;
        this.packer = packer;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Rotated UV islands pack densely without overlapping or losing gutter separation", () =>
        {
            var patches = Enumerable.Range(0, 36).Select(_ =>
            {
                var patch = new TexturePatch { Bounds = new SKRect(0, 0, 1, 1) };
                patch.Coordinates.Add(0, [0, 0]);
                patch.Coordinates.Add(1, [1, 0]);
                patch.Coordinates.Add(2, [0, 1]);
                patch.Triangles.Add([0, 1, 2]);
                return patch;
            }).ToArray();
            const int size = 512;
            const int gutter = 2;
            var scale = packer.Pack(patches, size, gutter);
            var owners = Enumerable.Repeat(-1, size * size).ToArray();
            var covered = 0;
            for (var chart = 0; chart < patches.Length; chart++)
            {
                var patch = patches[chart];
                var points = Enumerable.Range(0, 3).Select(v => patch.Coordinate(v, patch.QuarterTurns))
                    .Select(p => new SKPoint((float)(patch.AtlasBounds.Left + p.X * scale), (float)(patch.AtlasBounds.Top + p.Y * scale))).ToArray();
                for (var y = Math.Max(0, (int)patch.AtlasBounds.Top); y < Math.Min(size, Math.Ceiling(patch.AtlasBounds.Bottom)); y++)
                {
                    for (var x = Math.Max(0, (int)patch.AtlasBounds.Left); x < Math.Min(size, Math.Ceiling(patch.AtlasBounds.Right)); x++)
                    {
                        var signs = Enumerable.Range(0, 3).Select(i =>
                        {
                            var a = points[i];
                            var b = points[(i + 1) % 3];
                            return (b.X - a.X) * (y + .5 - a.Y) - (b.Y - a.Y) * (x + .5 - a.X);
                        }).ToArray();
                        if (!signs.All(v => v >= 0) && !signs.All(v => v <= 0))
                        {
                            continue;
                        }

                        Check(owners[y * size + x] == -1, "Triangle interiors do not overlap");
                        owners[y * size + x] = chart;
                        covered++;
                    }
                }
            }

            for (var y = gutter; y < size - gutter; y++)
            {
                for (var x = gutter; x < size - gutter; x++)
                {
                    var owner = owners[y * size + x];
                    if (owner < 0)
                    {
                        continue;
                    }

                    for (var dy = -gutter; dy <= gutter; dy++)
                    {
                        for (var dx = -gutter; dx <= gutter; dx++)
                        {
                            var adjacent = owners[(y + dy) * size + x + dx];
                            Check(adjacent < 0 || adjacent == owner, "Islands retain their sampling gutter");
                        }
                    }
                }
            }

            Console.WriteLine($"  Triangle-island atlas coverage: {100.0 * covered / (size * size):F1}%");
            Check(covered > size * size * .55, "Silhouette packing nests triangle islands instead of reserving empty rectangles");
        });
        tests.Run("Packed surface patches preserve projected colour and split UV seams", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "echo-texture-" + Guid.NewGuid().ToString("N"));
            var store = stores.Open(directory);
            store.Init("texture projection test");
            try
            {
                using var source = new SKBitmap(256, 256);
                for (var y = 0; y < 256; y++)
                {
                    for (var x = 0; x < 256; x++)
                    {
                        source.SetPixel(x, y, new SKColor((byte)x, (byte)y, 100));
                    }
                }

                images.Save(source, store.PathFor("source.png"), SKEncodedImageFormat.Png);
                var project = new ProjectData
                {
                    Images = [new Photo { Id = "a", Source = "source.png", Width = 256, Height = 256, FocalPixels = 128 }],
                    Settings = new Settings { TextureSize = 256, TextureChartFaces = 64, TextureJpegQuality = 100 }
                };
                var scene = new ReconstructionScene { Cameras = [new CameraPose { ImageId = "a" }] };
                var mesh = new Surface
                {
                    Vertices = [[-6, -6, 10], [-2, -6, 10], [-2, -2, 10], [-6, -2, 10],
                                [2, 2, 10], [6, 2, 10], [6, 6, 10], [2, 6, 10]],
                    Faces = [[0, 1, 2], [0, 2, 3], [4, 5, 6], [4, 6, 7]],
                    SourceImages = ["a", "a", "a", "a"]
                };
                foreach (var point in mesh.Vertices)
                {
                    point[2] += point[0] * .15 + point[1] * .1;
                }

                var output = store.PathFor("atlas.jpg");
                var result = baker.Bake(store, project, scene, mesh, output, default);
                var textured = result.Surface!;
                Check(result.ChartCount >= 2 && textured.Faces.Count == mesh.Faces.Count, "Multiple patches retain all faces");
                Check(result.Uvs.Count == textured.Vertices.Count, "One UV per textured vertex");
                Check(result.Uvs.All(uv => uv.All(v => v > 0 && v < 1)), "UVs retain border padding");
                using var atlas = SKBitmap.Decode(output);
                foreach (var face in textured.Faces)
                {
                    var uvX = face.Average(v => result.Uvs[v][0]);
                    var uvY = face.Average(v => result.Uvs[v][1]);
                    var depth = face.Average(v => textured.Vertices[v][2]);
                    var expectedX = face.Average(v => textured.Vertices[v][0]) / depth * 128 + 128;
                    var expectedY = face.Average(v => textured.Vertices[v][1]) / depth * 128 + 128;
                    var actual = atlas.GetPixel((int)(uvX * 256), (int)(uvY * 256));
                    Check(Math.Abs(actual.Red - expectedX) < 6 && Math.Abs(actual.Green - expectedY) < 6, "UV samples the correct source image location");
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        });
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
