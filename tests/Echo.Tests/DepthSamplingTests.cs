using Echo.Models;
using Echo.Depth;
using Echo.Features;
using Echo.Imaging;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Tests;

public sealed class DepthSamplingTests
{
    private readonly IDepthEstimator estimator;
    private readonly IProjectStoreFactory stores;
    private readonly IRasterImageService images;

    public DepthSamplingTests(IDepthEstimator estimator, IProjectStoreFactory stores, IRasterImageService images)
    {
        this.estimator = estimator;
        this.stores = stores;
        this.images = images;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Fine depth resists independent image noise while preserving a sharp surface boundary", () =>
        {
            var store = stores.Open(Path.Combine(Path.GetTempPath(), "echo-depth-sampling-" + Guid.NewGuid().ToString("N")));
            store.Init();
            try
            {
                var project = new ProjectData
                {
                    Settings = new Settings
                    {
                        DepthWidth = 1024,
                        DepthPlanes = 97,
                        DepthRefinementSamples = 18,
                        DepthNeighbours = 2,
                        DepthBackend = "cpu",
                        DepthRangeAutomatic = false,
                        DepthNear = 8,
                        DepthFar = 14
                    }
                };
                var scene = new ReconstructionScene();
                CreatePhotographs(store, project, scene);
                var map = estimator.Estimate(store, project, scene, scene.Cameras[0], default, 2);
                VerifySurfaces(map);
                if (Environment.GetEnvironmentVariable("ECHO_TEST_METAL") == "1")
                {
                    project.Settings.DepthBackend = "metal-validated";
                    var executions = new List<ProcessingExecution>();
                    var accelerated = estimator.Estimate(store, project, scene, scene.Cameras[0], default, 2, execution: executions.Add);
                    Check(executions.Any(item => item.LevelWidth == 1024 && item.AcceleratedSearches > 0), "Exercise native Metal with the fine-image prefilter");
                    Check(map.Depth.AsSpan().SequenceEqual(accelerated.Depth), "Validated Metal preserves CPU geometry");
                    Check(map.Confidence.AsSpan().SequenceEqual(accelerated.Confidence), "Validated Metal preserves CPU confidence");
                }

                using var blank = new SKBitmap(1024, 96);
                blank.Erase(new SKColor(127, 127, 127));
                foreach (var photo in project.Images)
                {
                    images.Save(blank, store.PathFor(photo.Source), SKEncodedImageFormat.Png);
                }
                Check(estimator.Estimate(store, project, scene, scene.Cameras[0], default, 2).Depth.All(depth => depth == 0),
                    "Prefiltering must not invent a surface in textureless photographs");
            }
            finally
            {
                Directory.Delete(store.Root, true);
            }
        });
    }

    private void CreatePhotographs(IProjectStore store, ProjectData project, ReconstructionScene scene)
    {
        const int width = 1024;
        const int height = 96;
        var random = new Random(741);
        var noise = Enumerable.Range(0, width * height).Select(_ => (float)random.NextDouble()).ToArray();
        var texture = new float[noise.Length];
        GaussianBlur.Apply(noise, texture, width, height, 1.2, default);
        foreach (var shift in new[] { 0, -24, 24 })
        {
            var id = "noisy" + shift;
            using var bitmap = new SKBitmap(width, height);
            for (var y = 0; y < height; y++)
            {
                // Upper and lower surfaces are at ten and twelve metres. Their
                // discontinuity stays on the same row in these horizontal views.
                var disparity = y < height / 2 ? shift : shift * 5 / 6;
                for (var x = 0; x < width; x++)
                {
                    var texel = texture[y * width + Math.Clamp(x + disparity, 0, width - 1)];
                    var value = (byte)Math.Clamp(127 + (texel - .5) * 640 + random.Next(-30, 31), 0, 255);
                    bitmap.SetPixel(x, y, new SKColor(value, value, value));
                }
            }
            images.Save(bitmap, store.PathFor(id + ".png"), SKEncodedImageFormat.Png);
            project.Images.Add(new Photo { Id = id, Source = id + ".png", Width = width, Height = height, FocalPixels = 768 });
            scene.Cameras.Add(new CameraPose { ImageId = id, Centre = [shift / 76.8, 0, 0] });
        }
    }

    private void VerifySurfaces(DepthMap map)
    {
        var errors = new List<double>();
        var boundarySamples = 0;
        var boundaryErrors = 0;
        for (var y = 12; y < map.Height - 12; y++)
        {
            for (var x = 24; x < map.Width - 24; x++)
            {
                var depth = map.Depth[y * map.Width + x];
                if (depth <= 0)
                {
                    continue;
                }
                var expected = y < map.Height / 2 ? 10 : 12;
                var error = Math.Abs(depth - expected);
                errors.Add(error);
                if (Math.Abs(y - map.Height / 2) <= 8)
                {
                    boundarySamples++;
                    if (error > .2)
                    {
                        boundaryErrors++;
                    }
                }
            }
        }
        errors.Sort();
        Check(errors.Count > 60000, "Fine matching must retain most of both noisy surfaces");
        Check(errors.Count(error => error > .2) < errors.Count / 80, "Recovered depths must agree with known geometry");
        Check(errors[errors.Count / 2] < .08 && errors[(int)(errors.Count * .95)] < .18, "Coverage must preserve depth precision");
        Check(boundarySamples > 12000, "Keep the small footprint near a real depth discontinuity");
        Check(boundaryErrors < boundarySamples / 40, "Do not smooth the ten- and twelve-metre surfaces together");
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
