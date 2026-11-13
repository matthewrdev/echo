using System.ComponentModel.Composition;
using System.Security.Cryptography;
using System.Text;
using Echo.Contracts;
using Echo.Depth;
using Echo.Geometry;
using Echo.Imaging;
using Echo.Infrastructure;
using SkiaSharp;

namespace Echo.Tests;

[Export]
public sealed class ProcessingEfficiencyTests
{
    private readonly IRasterImageService images;
    private readonly IDepthExecutionPlanner planner;
    private readonly IJsonCodec json;
    private readonly IFileOperations files;
    private readonly ICameraGeometry geometry;

    [ImportingConstructor]
    public ProcessingEfficiencyTests(IRasterImageService images, IDepthExecutionPlanner planner, IJsonCodec json, IFileOperations files, ICameraGeometry geometry)
    {
        this.images = images;
        this.planner = planner;
        this.json = json;
        this.files = files;
        this.geometry = geometry;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Grayscale pyramid spans preserve area averaging and channel rounding exactly", () =>
        {
            using var bitmap = new SKBitmap(173, 117);
            var pixels = new SKColor[173 * 117];
            var random = new Random(528);
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
            }
            bitmap.Pixels = pixels;
            foreach (var width in new[] { 32, 80, 173, 256 })
            {
                using var reference = images.Resize(bitmap, width);
                var direct = images.ResizeGray(new RasterPixels(173, 117, pixels), width);
                Check(direct.Width == reference.Width && direct.Height == reference.Height, "Same rounded dimensions");
                Check(direct.Pixels.SequenceEqual(images.Gray(reference)), "Identical grayscale samples");
            }
        });
        tests.Run("Native pixel spans preserve channel order, alpha conversion and padded rows", () =>
        {
            foreach (var colorType in new[] { SKColorType.Bgra8888, SKColorType.Rgba8888 })
            {
                foreach (var alphaType in new[] { SKAlphaType.Opaque, SKAlphaType.Premul, SKAlphaType.Unpremul })
                {
                    foreach (var padding in new[] { 0, 16 })
                    {
                        var info = new SKImageInfo(173, 117, colorType, alphaType);
                        using var bitmap = new SKBitmap(info, info.RowBytes + padding);
                        var random = new Random(528);
                        var pixels = new SKColor[bitmap.Width * bitmap.Height];
                        for (var i = 0; i < pixels.Length; i++)
                        {
                            var alpha = alphaType == SKAlphaType.Opaque ? 255 : random.Next(256);
                            pixels[i] = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)alpha);
                        }
                        bitmap.Pixels = pixels;
                        foreach (var width in new[] { 32, 80, 173, 256 })
                        {
                            using var reference = images.Resize(bitmap, width);
                            var direct = images.ResizeGray(bitmap, width);
                            Check(direct.Pixels.SequenceEqual(images.Gray(reference)), $"Identical pixels for {colorType}, {alphaType}, padding {padding}, width {width}");
                        }
                    }
                }
            }
        });
        tests.Run("Depth scheduling bounds nested workers and reduces concurrency for large images", () =>
        {
            var project = new ProjectData { Images = [new Photo { Width = 4032, Height = 2268 }] };
            project.Settings.DepthConcurrency = 8;
            project.Settings.DepthWorkerThreads = 12;
            project.Settings.DepthMemoryBudgetMb = 256;
            var plan = planner.Plan(project, 48);
            Check(plan.ConcurrentImages == 1 && plan.WorkersPerImage == 12, "Memory bounds concurrent image jobs");
            project.Settings.DepthMemoryBudgetMb = 4096;
            plan = planner.Plan(project, 48);
            Check(plan.ConcurrentImages > 1 && plan.ConcurrentImages * plan.WorkersPerImage <= 12, "Shared CPU budget");
        });
        tests.Run("Streamed JSON keeps checkpoint hashes and bytes compatible and writes atomically", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "echo-stream-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var value = new DepthMap { ImageId = "é-photo", Width = 2, Height = 1, Depth = [1.25f, 0], Confidence = [.9f, 0] };
                var expected = Encoding.UTF8.GetBytes(json.Encode(value));
                Check(files.Hash(value) == Convert.ToHexStringLower(SHA256.HashData(expected)), "Stable fingerprint");
                files.AtomicJson(path, value);
                Check(File.ReadAllBytes(path).SequenceEqual(expected), "Stable JSON bytes");
                Check(json.Read<DepthMap>(path).Depth.SequenceEqual(value.Depth), "Stream reader");
                try
                {
                    files.AtomicWrite(path, stream => { stream.WriteByte(1); throw new IOException("injected failure"); });
                }
                catch (IOException)
                {
                }
                Check(File.ReadAllBytes(path).SequenceEqual(expected), "Previous checkpoint survives failed write");
            }
            finally
            {
                File.Delete(path);
            }
        });
        tests.Run("Cached camera projection preserves the original projection and cheirality", () =>
        {
            var photo = new Photo { Width = 4032, Height = 2268, FocalPixels = 3000 };
            var camera = new CameraPose { Centre = [1, 2, 3], Rotation = geometry.AttitudeRotation(new Attitude { Yaw = 24, Pitch = -17 }) };
            var projector = new CameraProjector(geometry, photo, camera);
            var random = new Random(124);
            for (var i = 0; i < 100; i++)
            {
                var point = new V3(random.NextDouble() * 20 - 10, random.NextDouble() * 20 - 10, random.NextDouble() * 30 - 15);
                var original = geometry.Project(photo, camera, point);
                var cached = projector.Project(point);
                Check((original == null) == !cached.HasValue, "Same behind-camera handling");
                if (original != null)
                {
                    Check(original.SequenceEqual(cached!.Value.Array), "Identical coordinates");
                }
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
