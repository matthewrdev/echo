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
    private readonly ITextureRasterizer rasterizer;
    private readonly ITextureSeamLeveler seamLeveler;
    private readonly ITexturePatchBuilder patchBuilder;

    [ImportingConstructor]
    public TextureTests(ITextureBaker baker, IProjectStoreFactory stores, IRasterImageService images,
        ITexturePatchPacker packer, ITextureRasterizer rasterizer, ITextureSeamLeveler seamLeveler,
        ITexturePatchBuilder patchBuilder)
    {
        this.baker = baker;
        this.stores = stores;
        this.images = images;
        this.packer = packer;
        this.rasterizer = rasterizer;
        this.seamLeveler = seamLeveler;
        this.patchBuilder = patchBuilder;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Photographic colour finishing preserves neutrals, alpha, hue order and an exact off mode", () =>
        {
            SKColor[] colours = [SKColors.Black, SKColors.White, new(64, 64, 64), new(192, 192, 192),
                new(140, 100, 60), new(60, 100, 50), new(230, 80, 20), new(10, 40, 60, 0)];
            foreach (var type in new[] { SKColorType.Bgra8888, SKColorType.Rgba8888 })
            {
                using var bitmap = new SKBitmap(new SKImageInfo(colours.Length, 1, type, SKAlphaType.Unpremul));
                bitmap.Pixels = colours;
                var originalPixels = bitmap.Pixels;
                var bytes = bitmap.GetPixelSpan().ToArray();
                TextureColourEnhancer.Apply(bitmap, 0, default);
                Check(bitmap.GetPixelSpan().SequenceEqual(bytes), "Disabled finishing leaves every byte unchanged");
                TextureColourEnhancer.Apply(bitmap, .5, default);
                var output = bitmap.Pixels;
                Check(output[0] == SKColors.Black && output[1] == SKColors.White, "Black and white endpoints remain fixed");
                Check(output[2].Red > colours[2].Red && output[3].Red > colours[3].Red, "The finish lifts dim midtones while preserving its black and white endpoints");
                Check(output.Take(4).All(pixel => pixel.Red == pixel.Green && pixel.Green == pixel.Blue), "Neutral colours do not acquire a cast");
                Check(output[4].Red > output[4].Green && output[4].Green > output[4].Blue, "Warm rock colours retain their hue order");
                Check(output[5].Green > output[5].Red && output[5].Red > output[5].Blue, "Vegetation retains its hue order");
                Check(output[7] == originalPixels[7], "Transparent unused atlas pixels remain unchanged");
                Expect<ArgumentOutOfRangeException>(() => TextureColourEnhancer.Apply(bitmap, double.NaN, default));
                Expect<OperationCanceledException>(() => TextureColourEnhancer.Apply(bitmap, .5, new CancellationToken(true)));
            }
        });
        tests.Run("Seam softening follows UV-split geometric neighbours and preserves chart interiors", () =>
        {
            const int size = 64;
            var pixels = Enumerable.Range(0, size * size).Select(index =>
                new SKColor((byte)(index % size < 32 ? 100 : 140), 80, 60)).ToArray();
            var before = pixels.ToArray();
            var mesh = new Surface
            {
                Vertices = [[0, 0, 0], [1, 0, 0], [0, 1, 0], [1, 0, 0], [1, 1, 0], [0, 1, 0]],
                Faces = [[0, 1, 2], [3, 4, 5]],
                SourceImages = ["a", "b"]
            };
            double[][] uvs = [[8d / size, 8d / size], [24d / size, 8d / size], [8d / size, 24d / size],
                [40d / size, 8d / size], [56d / size, 24d / size], [40d / size, 24d / size]];
            TextureSeamSoftener.Apply(pixels, size, mesh, uvs, 0, default);
            Check(pixels.SequenceEqual(before), "Off is exactly byte preserving");
            TextureSeamSoftener.Apply(pixels, size, mesh, uvs, 4, default);
            var difference = Math.Abs(pixels[16 * size + 41].Red - pixels[15 * size + 15].Red);
            Check(difference < 24, "Matched seam strips move towards the same colour despite different UV orientations");
            Check(pixels[10 * size + 10] == before[10 * size + 10] && pixels[20 * size + 50] == before[20 * size + 50],
                "Photographic interiors remain exactly unchanged");
            Check(pixels.All(pixel => pixel.Green == 80 && pixel.Blue == 60 && pixel.Alpha == 255), "Unchanged channels and coverage remain intact");
            mesh.SourceImages[1] = "a";
            var same = before.ToArray();
            TextureSeamSoftener.Apply(same, size, mesh, uvs, 4, default);
            Check(same.SequenceEqual(before), "UV packing boundaries alone do not trigger photographic softening");
            Expect<OperationCanceledException>(() => TextureSeamSoftener.Apply(same, size, mesh, uvs, 4, new CancellationToken(true)));
            Expect<ArgumentOutOfRangeException>(() => TextureSeamSoftener.Apply(same, size, mesh, uvs, 17, default));
        });
        tests.Run("Sky suppression changes baked skyline colour without changing the surface or source image", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "echo-sky-texture-" + Guid.NewGuid().ToString("N"));
            var store = stores.Open(directory);
            store.Init("sky guard");
            try
            {
                using var photo = new SKBitmap(128, 128);
                photo.Pixels = Enumerable.Range(0, 128 * 128).Select(index => index / 128 < 64
                    ? new SKColor(100, 170, 230) : new SKColor(80, 110, 60)).ToArray();
                images.Save(photo, store.PathFor("source.png"), SKEncodedImageFormat.Png);
                var sourceBytes = File.ReadAllBytes(store.PathFor("source.png"));
                var project = new ProjectData
                {
                    Images = [new Photo { Id = "a", Source = "source.png", Width = 128, Height = 128, FocalPixels = 64 }],
                    Settings = new Settings { TextureSuppressSky = false, TextureColourEnhancement = 0, TextureSeamBlendPixels = 0, TextureSize = 256, TextureJpegQuality = 100 }
                };
                var scene = new ReconstructionScene { Cameras = [new CameraPose { ImageId = "a" }] };
                var mesh = new Surface
                {
                    Vertices = [[-1, -.3, 10], [1, -.3, 10], [1, .3, 10], [-1, .3, 10]],
                    Faces = [[0, 1, 2], [0, 2, 3]],
                    SourceImages = ["a", "a"]
                };
                var before = baker.Bake(store, project, scene, mesh, store.PathFor("before.jpg"), default);
                project.Settings.TextureSuppressSky = true;
                var after = baker.Bake(store, project, scene, mesh, store.PathFor("after.jpg"), default);
                var face = before.Surface!.Faces[0];
                var x = (int)(face.Average(vertex => before.Uvs[vertex][0]) * 256);
                var y = (int)(face.Average(vertex => before.Uvs[vertex][1]) * 256);
                using var first = SKBitmap.Decode(store.PathFor("before.jpg"));
                using var second = SKBitmap.Decode(store.PathFor("after.jpg"));
                Check(first.GetPixel(x, y).Blue > 150 && second.GetPixel(x, y).Blue < 90, "Baking actually uses the guarded foreground sample");
                Check(before.Uvs.Zip(after.Uvs).All(pair => pair.First.SequenceEqual(pair.Second)), "UV layout is unchanged");
                Check(after.Surface!.Faces.Count == mesh.Faces.Count && mesh.SourceImages.All(id => id == "a"), "No face or source assignment is removed");
                Check(sourceBytes.SequenceEqual(File.ReadAllBytes(store.PathFor("source.png"))), "Original photographs are never edited");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        });
        tests.Run("Unseen completed regions keep all geometry and smoothly inherit colour across UV splits", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "echo-colour-completion-" + Guid.NewGuid().ToString("N"));
            var store = stores.Open(directory);
            store.Init("colour completion test");
            try
            {
                using var bitmap = new SKBitmap(128, 128);
                bitmap.Erase(new SKColor(54, 132, 78));
                images.Save(bitmap, store.PathFor("a.png"), SKEncodedImageFormat.Png);
                var project = new ProjectData
                {
                    Images = [new Photo { Id = "a", Source = "a.png", Width = 128, Height = 128, FocalPixels = 64 }],
                    Settings = new Settings { TextureColourEnhancement = 0, TextureSeamBlendPixels = 0, TextureSize = 256, TextureChartFaces = 2, TextureJpegQuality = 100 }
                };
                var scene = new ReconstructionScene { Cameras = [new CameraPose { ImageId = "a" }] };
                var mesh = new Surface
                {
                    Vertices = [[-2, -2, 10], [2, -2, 10], [2, 2, 10], [-2, 2, 10], [0, 0, 10],
                        [-2, -2, 10], [2, -2, 10], [-2, -4, 10], [2, -4, 10]],
                    Faces = [[0, 1, 4], [1, 2, 4], [2, 3, 4], [3, 0, 4], [5, 7, 8], [5, 8, 6]],
                    SourceImages = ["", "", "", "", "a", "a"]
                };
                var output = store.PathFor("atlas.jpg");
                var texture = baker.Bake(store, project, scene, mesh, output, default);
                Check(texture.Surface!.Faces.Count == 6, "Texturing retains the complete surface");
                Check(texture.Surface.SourceImages.Count(string.IsNullOrEmpty) == 4, "Inferred colours are not labelled as measured photographs");
                using var atlas = SKBitmap.Decode(output);
                foreach (var face in texture.Surface.Faces)
                {
                    var x = face.Average(vertex => texture.Uvs[vertex][0]) * texture.Size;
                    var y = face.Average(vertex => texture.Uvs[vertex][1]) * texture.Size;
                    var colour = atlas.GetPixel((int)x, (int)y);
                    var matches = Math.Abs(colour.Red - 54) < 4 && Math.Abs(colour.Green - 132) < 4 && Math.Abs(colour.Blue - 78) < 4;
                    Check(matches, "Missing-photo triangles use surrounding surface colour across all UV charts");
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        });
        tests.Run("Grazing photographic views retain undistorted planar UV charts", () =>
        {
            var project = new ProjectData
            {
                Images = [new Photo { Id = "a", Width = 128, Height = 128, FocalPixels = 64 }]
            };
            var scene = new ReconstructionScene { Cameras = [new CameraPose { ImageId = "a" }] };
            var mesh = new Surface
            {
                Vertices = [[.1, -2, 10], [.1, 2, 10], [.1, 2, 20], [.1, -2, 20]],
                Faces = [[0, 1, 2], [0, 2, 3]],
                SourceImages = ["a", "a"]
            };
            var patches = patchBuilder.Build(project, scene, mesh, default);
            Check(patches.Count == 1 && patches[0].Faces.Count == 2, "A grazing view does not split or discard a flat region");
            var coordinates = patches[0].Coordinates;
            foreach (var face in mesh.Faces)
            {
                var a = coordinates[face[0]];
                var b = coordinates[face[1]];
                var c = coordinates[face[2]];
                var area = Math.Abs((b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])) * .5;
                Check(Math.Abs(area - 20) < 1e-5, "The tangent chart preserves surface area instead of collapsing its UVs");
            }
        });
        tests.Run("Source-view UV charts follow smooth relief without breaking into triangular islands", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "echo-curved-chart-" + Guid.NewGuid().ToString("N"));
            var store = stores.Open(directory);
            store.Init("curved chart test");
            try
            {
                using var bitmap = new SKBitmap(128, 128);
                bitmap.Erase(SKColors.CornflowerBlue);
                images.Save(bitmap, store.PathFor("a.png"), SKEncodedImageFormat.Png);
                var project = new ProjectData
                {
                    Images = [new Photo { Id = "a", Source = "a.png", Width = 128, Height = 128, FocalPixels = 70 }],
                    Settings = new Settings { TextureColourEnhancement = 0, TextureSeamBlendPixels = 0, TextureSize = 256, TextureChartFaces = 256, TextureChartAngle = 45 }
                };
                var scene = new ReconstructionScene { Cameras = [new CameraPose { ImageId = "a" }] };
                project.Images.Add(new Photo { Id = "b", Source = "a.png", Width = 128, Height = 128, FocalPixels = 70 });
                scene.Cameras.Add(new CameraPose { ImageId = "b" });
                var mesh = new Surface();
                for (var y = 0; y <= 4; y++)
                {
                    for (var x = 0; x <= 8; x++)
                    {
                        var horizontal = (x - 4) * 1.25;
                        mesh.Vertices.Add([horizontal, y - 2, 10 + 2 * Math.Cos(horizontal * .5)]);
                    }
                }
                for (var y = 0; y < 4; y++)
                {
                    for (var x = 0; x < 8; x++)
                    {
                        var a = y * 9 + x;
                        mesh.Faces.Add([a, a + 1, a + 10]);
                        mesh.Faces.Add([a, a + 10, a + 9]);
                        mesh.SourceImages.Add("a");
                        mesh.SourceImages.Add("b");
                    }
                }
                var result = baker.Bake(store, project, scene, mesh, store.PathFor("atlas.jpg"), default);
                Check(result.ChartCount == 1, "A visible curved region stays one UV chart even when source photographs change");
                Check(result.Surface!.Faces.Count == 64, "Chart construction retains the complete curved surface");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        });
        tests.Run("Photographic seam levelling removes exposure steps while preserving image detail", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "echo-seam-" + Guid.NewGuid().ToString("N"));
            var store = stores.Open(directory);
            store.Init("seam correction test");
            try
            {
                var project = new ProjectData();
                var scene = new ReconstructionScene();
                foreach (var id in new[] { "a", "b" })
                {
                    using var bitmap = new SKBitmap(64, 64);
                    for (var y = 0; y < 64; y++)
                    {
                        for (var x = 0; x < 64; x++)
                        {
                            var intensity = (byte)((id == "a" ? 80 : 130) + (y % 8 < 4 ? 30 : 0));
                            bitmap.SetPixel(x, y, new SKColor(intensity, intensity, intensity));
                        }
                    }
                    images.Save(bitmap, store.PathFor(id + ".png"), SKEncodedImageFormat.Png);
                    project.Images.Add(new Photo { Id = id, Source = id + ".png", Width = 64, Height = 64, FocalPixels = 32 });
                    scene.Cameras.Add(new CameraPose { ImageId = id });
                }
                var mesh = new Surface
                {
                    Vertices = [[-4, -2, 10], [0, -2, 10], [0, 2, 10], [-4, 2, 10],
                        [0, -2, 10], [4, -2, 10], [4, 2, 10], [0, 2, 10]],
                    Faces = [[0, 1, 2], [0, 2, 3], [4, 5, 6], [4, 6, 7]],
                    SourceImages = ["a", "a", "b", "b"]
                };
                var correction = seamLeveler.Compute(store, project, scene, mesh, default);
                Check(correction.SeamEdges == 1, "Camera-split duplicate vertices share a seam");
                Check(Math.Abs(correction.MeanDifferenceBefore - 50) < 1e-8, "The measured exposure step is fifty intensity levels");
                Check(correction.MeanDifferenceAfter < .01, "The corrected seam matches on both sides");
                for (var corner = 0; corner < correction.CornerOffsets.Length; corner++)
                {
                    var expected = corner < 6 ? 25 : -25;
                    Check(Math.Abs(correction.CornerOffsets[corner].X - expected) < .1,
                        "A constant exposure difference gives a constant offset, preserving the thirty-level stripe contrast");
                }
                Check(mesh.Vertices.Count == 8 && mesh.Faces.Count == 4, "Colour correction does not change geometry");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        });
        tests.Run("Atlas gutters copy nearest source texels and shared triangle edges are antialiased", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "echo-texture-edges-" + Guid.NewGuid().ToString("N"));
            var store = stores.Open(directory);
            store.Init("texture edge test");
            try
            {
                using var source = new SKBitmap(64, 64);
                for (var y = 0; y < 64; y++)
                {
                    for (var x = 0; x < 64; x++)
                    {
                        source.SetPixel(x, y, new SKColor((byte)(x * 4), (byte)(y * 4), 100));
                    }
                }
                images.Save(source, store.PathFor("source.png"), SKEncodedImageFormat.Png);
                var project = new ProjectData
                {
                    Images = [new Photo { Id = "a", Source = "source.png", Width = 64, Height = 64, FocalPixels = 32 }],
                    Settings = new Settings { TextureGutter = 4 }
                };
                var scene = new ReconstructionScene { Cameras = [new CameraPose { ImageId = "a" }] };
                var mesh = new Surface
                {
                    Vertices = [[-7.5, -7.5, 10], [7.5, -7.5, 10], [7.5, 7.5, 10], [-7.5, 7.5, 10]],
                    Faces = [[0, 1, 2], [0, 2, 3]],
                    SourceImages = ["a", "a"]
                };
                var texture = new TextureResult
                {
                    Size = 64,
                    Uvs = [[.125, .125], [.875, .125], [.875, .875], [.125, .875]]
                };
                using var atlas = new SKBitmap(64, 64);
                rasterizer.Paint(store, project, scene, mesh, texture, [], atlas, default);
                Check(atlas.GetPixel(59, 30) == atlas.GetPixel(55, 30), "Padding takes the nearest boundary colour without diagonal smearing");
                Check(atlas.GetPixel(30, 59) == atlas.GetPixel(30, 55), "Padding is symmetric along both axes");
                Check(atlas.GetPixel(63, 30).Alpha == 255 && atlas.GetPixel(63, 30).Blue == 100,
                    "Unused atlas space continues the photographic colours instead of forming a dark outline");

                source.Erase(SKColors.Red);
                images.Save(source, store.PathFor("source.png"), SKEncodedImageFormat.Png);
                source.Erase(SKColors.Blue);
                images.Save(source, store.PathFor("blue.png"), SKEncodedImageFormat.Png);
                project.Images.Add(new Photo { Id = "b", Source = "blue.png", Width = 64, Height = 64, FocalPixels = 32 });
                scene.Cameras.Add(new CameraPose { ImageId = "b" });
                mesh.SourceImages[1] = "b";
                rasterizer.Paint(store, project, scene, mesh, texture, [], atlas, default);
                var edge = atlas.GetPixel(30, 30);
                Check(edge.Red is > 100 and < 155 && edge.Blue is > 100 and < 155, "Subpixel coverage blends a shared diagonal rather than choosing the last triangle");
                Check(atlas.GetPixel(40, 20) == SKColors.Red && atlas.GetPixel(20, 40) == SKColors.Blue, "Antialiasing preserves triangle interiors");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        });
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
                    Settings = new Settings { TextureColourEnhancement = 0, TextureSeamBlendPixels = 0, TextureSize = 256, TextureChartFaces = 64, TextureJpegQuality = 100 }
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
                SKColor[]? expectedPixels = null;
                foreach (var padding in new[] { 16, 0 })
                {
                    foreach (var colourType in new[] { SKColorType.Rgba8888, SKColorType.Bgra8888 })
                    {
                        foreach (var alphaType in new[] { SKAlphaType.Premul, SKAlphaType.Unpremul, SKAlphaType.Opaque })
                        {
                            var info = new SKImageInfo(256, 256, colourType, alphaType);
                            using var nativeAtlas = new SKBitmap(info, info.RowBytes + padding);
                            nativeAtlas.Erase(SKColors.Magenta);
                            rasterizer.Paint(store, project, scene, textured, result, [], nativeAtlas, default);
                            var actualPixels = nativeAtlas.Pixels;
                            expectedPixels ??= actualPixels;
                            Check(actualPixels.AsSpan().SequenceEqual(expectedPixels), "Borrowed atlas buffers match fallback pixels across channel orders, alpha layouts and row strides");
                        }
                    }
                }
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

    private void Expect<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new Exception($"Expected {typeof(T).Name}.");
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
