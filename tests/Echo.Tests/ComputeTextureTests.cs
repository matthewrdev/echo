using Echo.Compute;
using Echo.Geometry;
using Echo.Imaging;
using Echo.Models;
using Echo.Projects;
using Echo.Texturing.Rasterization;
using Echo.Texturing.Seams;
using Echo.Texturing;
using SkiaSharp;

namespace Echo.Tests;

public sealed class ComputeTextureTests
{
    private readonly IComputeService compute;
    private readonly IProjectStoreFactory stores;
    private readonly IRasterImageService images;
    private readonly ITextureRasterizer rasterizer;

    public ComputeTextureTests(IComputeService compute, IProjectStoreFactory stores, IRasterImageService images, ITextureRasterizer rasterizer)
    {
        this.compute = compute;
        this.stores = stores;
        this.images = images;
        this.rasterizer = rasterizer;
    }

    internal void Run(TestSuite tests)
    {
        using (var probe = compute.Open(ComputeBackend.Auto, default))
        {
            if (probe == null)
            {
                return;
            }
        }

        tests.Run("Metal texture projection preserves gradients, seams, channel order and fallback", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "echo-compute-texture-" + Guid.NewGuid().ToString("N"));
            var store = stores.Open(directory);
            store.Init();
            try
            {
                using var source = new SKBitmap(64, 64);
                source.Pixels = Enumerable.Range(0, 4096).Select(i => new SKColor((byte)(i % 64 * 4), (byte)(i / 64 * 4), 99)).ToArray();
                images.Save(source, store.PathFor("source.png"), SKEncodedImageFormat.Png);
                var project = new ProjectData
                {
                    Images = [new Photo { Id = "a", Source = "source.png", Width = 64, Height = 64, FocalPixels = 32 }],
                    Settings = new Settings { TextureGutter = 4, ComputeBackend = ComputeBackend.Cpu }
                };
                var scene = new ReconstructionScene { Cameras = [new CameraPose { ImageId = "a" }] };
                var mesh = new Surface
                {
                    Vertices = [[-7, -7, 10], [7, -7, 12], [7, 7, 12], [-7, 7, 10]],
                    Faces = [[0, 1, 2], [0, 2, 3]],
                    SourceImages = ["a", "a"]
                };
                var texture = new TextureResult
                {
                    Size = 256,
                    Uvs = [[.125, .125], [.875, .125], [.875, .875], [.125, .875]],
                    Corrections = new TextureSeamCorrections { CornerOffsets = Enumerable.Repeat(new V3(3, -2, 1), 6).ToArray() }
                };
                using var expected = new SKBitmap(256, 256);
                var coverage = rasterizer.Paint(store, project, scene, mesh, texture, [], expected, default);
                foreach (var colourType in new[] { SKColorType.Bgra8888, SKColorType.Rgba8888 })
                {
                    using var actual = new SKBitmap(new SKImageInfo(256, 256, colourType, SKAlphaType.Premul));
                    project.Settings.ComputeBackend = ComputeBackend.Metal;
                    var measured = rasterizer.Paint(store, project, scene, mesh, texture, [], actual, default);
                    Check(coverage == measured, "Atlas coverage differs");
                    Check(expected.Pixels.Zip(actual.Pixels).All(pair => Math.Abs(pair.First.Red - pair.Second.Red) <= 2
                        && Math.Abs(pair.First.Green - pair.Second.Green) <= 2 && Math.Abs(pair.First.Blue - pair.Second.Blue) <= 2
                        && pair.Second.Alpha == 255), "Texture colour or channel layout differs");
                    project.Settings.ComputeBackend = ComputeBackend.Auto;
                    using var limited = compute.BeginScope(16);
                    rasterizer.Paint(store, project, scene, mesh, texture, [], actual, default);
                    Check(expected.Pixels.SequenceEqual(actual.Pixels), "Failed GPU paint did not restart atomically on CPU");
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
