using System.ComponentModel.Composition;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

    [ImportingConstructor]
    public ProcessingEfficiencyTests(IRasterImageService images, IDepthExecutionPlanner planner, IJsonCodec json, IFileOperations files)
    {
        this.images = images;
        this.planner = planner;
        this.json = json;
        this.files = files;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Bounded RANSAC sampling preserves seeded stable random-key selection", () =>
        {
            foreach (var count in new[] { 8, 19, 600, 3000 })
            {
                var source = Enumerable.Range(0, count).ToArray();
                var expectedRandom = new Random(817);
                var actualRandom = new Random(817);
                var selected = new int[8];
                for (var iteration = 0; iteration < 160; iteration++)
                {
                    var expected = source.OrderBy(_ => expectedRandom.Next()).Take(8).ToArray();
                    RandomSampleSelector.Select<int>(source, actualRandom, selected);
                    Check(selected.SequenceEqual(expected), "Same observations, ordering and random draw count");
                }
            }
        });
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
                using var reference = ReferenceResize(bitmap, width);
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
                            using var reference = ReferenceResize(bitmap, width);
                            var direct = images.ResizeGray(bitmap, width);
                            Check(images.ResizePixels(bitmap, width).Pixels.SequenceEqual(reference.Pixels), "Identical resized colours");
                            Check(direct.Pixels.SequenceEqual(images.Gray(reference)), $"Identical pixels for {colorType}, {alphaType}, padding {padding}, width {width}");
                        }
                    }
                }
            }
        });
        tests.Run("Automatic depth memory scales with runtime capacity and preserves manual overrides", () =>
        {
            const long megabyte = 1024 * 1024;
            const long gigabyte = 1024 * megabyte;
            Check(DepthMemoryBudget.ResolveMegabytes(0, 8 * gigabyte) == 2048, "8 GB runtime capacity budgets 2 GB");
            Check(DepthMemoryBudget.ResolveMegabytes(0, 16 * gigabyte) == 4096, "16 GB runtime capacity budgets 4 GB");
            Check(DepthMemoryBudget.ResolveMegabytes(0, 64 * gigabyte) == 16384, "Larger machines are not capped at 4 GB");
            Check(DepthMemoryBudget.ResolveMegabytes(0, long.MaxValue) == 32768, "Automatic budget stays bounded without overflow");
            Check(DepthMemoryBudget.ResolveMegabytes(0, 512 * megabyte) == 256, "Minimum scheduling budget");
            Check(DepthMemoryBudget.ResolveMegabytes(0, 0) == 1024, "Conservative fallback when runtime capacity is unavailable");
            Check(DepthMemoryBudget.ResolveMegabytes(2048, 64 * gigabyte) == 2048, "Explicit 2 GB remains explicit");
            Check(DepthMemoryBudget.ResolveMegabytes(4096, 8 * gigabyte) == 4096, "Explicit 4 GB remains explicit");
        });
        tests.Run("Depth memory settings accept automatic mode and retain saved manual budgets", () =>
        {
            var automatic = JsonSerializer.Deserialize<Settings>("{}", json.Options)!;
            automatic.Validate();
            Check(automatic.DepthMemoryBudgetMb == 0, "Missing memory settings default to automatic");
            Check(json.Clone(automatic).DepthMemoryBudgetMb == 0, "Automatic mode round-trips");
            Check(JsonSerializer.Deserialize<Settings>("{\"depthMemoryBudgetMb\":1024}", json.Options)!.DepthMemoryBudgetMb == 1024, "Old projects retain explicit budgets");
            foreach (var budget in new[] { -1, 1, 255, 32769 })
            {
                var settings = new Settings { DepthMemoryBudgetMb = budget };
                var rejected = false;
                try
                {
                    settings.Validate();
                }
                catch (EchoException)
                {
                    rejected = true;
                }
                Check(rejected, $"Reject invalid manual budget {budget}");
            }

            var project = new ProjectData { Images = [new Photo { Width = 4032, Height = 2268 }] };
            project.Settings.DepthWidth = 2048;
            project.Settings.DepthNeighbours = 12;
            project.Settings.DepthWorkerThreads = 16;
            project.Settings.DepthBackend = "cpu";
            var automaticPlan = planner.Plan(project, 48);
            Check(automaticPlan.MemoryBudgetBytes >= 256L * 1024 * 1024, "Resolve automatic mode before scheduling");
            Check(automaticPlan.ConcurrentImages * automaticPlan.WorkersPerImage <= 16, "Automatic memory retains the CPU budget");
            project.Settings.DepthMemoryBudgetMb = 1024;
            Check(planner.Plan(project, 48).ConcurrentImages == 1, "1 GB bounds large depth jobs to one image");
            foreach (var budget in new[] { 2048, 4096 })
            {
                project.Settings.DepthMemoryBudgetMb = budget;
                var plan = planner.Plan(project, 48);
                Check(plan.ConcurrentImages == 2 && plan.WorkersPerImage == 8, "2 GB and 4 GB allow two jobs sharing 16 workers");
                Check(plan.MemoryBudgetBytes == budget * 1024L * 1024, "Report the resolved manual budget");
            }
            var cpuPlan = planner.Plan(project, 48);
            project.Settings.DepthBackend = "auto";
            var metalPlan = planner.Plan(project, 48);
            Check(metalPlan.EstimatedBytesPerImage > cpuPlan.EstimatedBytesPerImage, "Automatic reserves GPU uploads, resident images and batches");
            Check(metalPlan.MemoryBudgetBytes == cpuPlan.MemoryBudgetBytes, "Backend choice preserves the explicit memory budget");
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
            var camera = new CameraPose { Centre = [1, 2, 3], Rotation = CameraGeometry.AttitudeRotation(new Attitude { Yaw = 24, Pitch = -17 }) };
            var projector = new CameraProjector(photo, camera);
            var random = new Random(124);
            for (var i = 0; i < 100; i++)
            {
                var point = new V3(random.NextDouble() * 20 - 10, random.NextDouble() * 20 - 10, random.NextDouble() * 30 - 15);
                var original = CameraGeometry.Project(photo, camera, point);
                var cached = projector.Project(point);
                Check((original == null) == !cached.HasValue, "Same behind-camera handling");
                if (original != null)
                {
                    Check(original.SequenceEqual(cached!.Value.Array), "Identical coordinates");
                }
            }
        });
    }

    private SKBitmap ReferenceResize(SKBitmap source, int width)
    {
        // Independent pre-optimisation area-average implementation.
        var scale = Math.Min(1.0, (double)width / source.Width);
        var result = new SKBitmap(Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)));
        var input = source.Pixels;
        var output = new SKColor[result.Width * result.Height];
        for (var y = 0; y < result.Height; y++)
        {
            for (var x = 0; x < result.Width; x++)
            {
                var x0 = x * source.Width / result.Width;
                var x1 = (x + 1) * source.Width / result.Width;
                var y0 = y * source.Height / result.Height;
                var y1 = (y + 1) * source.Height / result.Height;
                long red = 0;
                long green = 0;
                long blue = 0;
                var count = (x1 - x0) * (y1 - y0);
                for (var sy = y0; sy < y1; sy++)
                {
                    for (var sx = x0; sx < x1; sx++)
                    {
                        var colour = input[sy * source.Width + sx];
                        red += colour.Red;
                        green += colour.Green;
                        blue += colour.Blue;
                    }
                }
                output[y * result.Width + x] = new SKColor((byte)(red / count), (byte)(green / count), (byte)(blue / count));
            }
        }
        result.Pixels = output;
        return result;
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
