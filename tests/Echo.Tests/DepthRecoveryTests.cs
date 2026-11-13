using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Depth;
using Echo.Imaging;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Tests;

[Export]
public sealed class DepthRecoveryTests
{
    private readonly IDepthEstimator estimator;
    private readonly IProjectStoreFactory stores;
    private readonly IRasterImageService images;

    [ImportingConstructor]
    public DepthRecoveryTests(IDepthEstimator estimator, IProjectStoreFactory stores, IRasterImageService images)
    {
        this.estimator = estimator;
        this.stores = stores;
        this.images = images;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Depth recovers fine stereo texture after an empty coarse level without filling blank pixels", () =>
        {
            var store = stores.Open(Path.Combine(Path.GetTempPath(), "echo-depth-recovery-" + Guid.NewGuid().ToString("N")));
            store.Init();
            try
            {
                var project = new ProjectData
                {
                    Settings = new Settings { DepthWidth = 320, DepthPlanes = 101, DepthNeighbours = 2, DepthRangeAutomatic = false, DepthNear = 8, DepthFar = 12 }
                };
                var scene = new ReconstructionScene();
                using var texture = new SKBitmap(320, 192);
                var random = new Random(56);
                for (var y = 0; y < texture.Height; y += 2)
                {
                    for (var x = 0; x < texture.Width; x += 2)
                    {
                        // Every 2x2 block averages to exactly grey at 160px, while
                        // the full-resolution image contains unique matchable texture.
                        var first = random.Next(-90, 91);
                        var second = random.Next(-90, 91);
                        SetGray(texture, x, y, 127 + first);
                        SetGray(texture, x + 1, y, 127 + second);
                        SetGray(texture, x, y + 1, 127 - second);
                        SetGray(texture, x + 1, y + 1, 127 - first);
                    }
                }
                foreach (var shift in new[] { 0, -6, 6 })
                {
                    var id = "view" + shift;
                    using var bitmap = new SKBitmap(texture.Width, texture.Height);
                    for (var y = 0; y < bitmap.Height; y++)
                    {
                        for (var x = 0; x < bitmap.Width; x++)
                        {
                            bitmap.SetPixel(x, y, texture.GetPixel(Math.Clamp(x + shift, 0, texture.Width - 1), y));
                        }
                    }
                    images.Save(bitmap, store.PathFor(id + ".png"), SKEncodedImageFormat.Png);
                    project.Images.Add(new Photo { Id = id, Source = id + ".png", Width = 320, Height = 192, FocalPixels = 240 });
                    scene.Cameras.Add(new CameraPose { ImageId = id, Centre = [shift / 24.0, 0, 0] });
                }
                var phases = new List<DepthLevelMetrics>();
                var map = estimator.Estimate(store, project, scene, scene.Cameras[0], default, 2, metrics: phases.Add);
                Check(phases[0].RetainedSamples == 0, "Coarse image must have no usable depth seeds");
                Check(phases[1].RecoveredSamples > 40000, "Fine detail must be searched despite the empty coarse map");
                var valid = 0;
                var incorrect = 0;
                for (var y = 10; y < map.Height - 10; y++)
                {
                    for (var x = 12; x < map.Width - 12; x++)
                    {
                        var depth = map.Depth[y * map.Width + x];
                        if (depth > 0)
                        {
                            valid++;
                            if (Math.Abs(depth - 10) > .15)
                            {
                                incorrect++;
                            }
                        }
                    }
                }
                Check(valid > 40000, "Most mutually visible fine texture should have depth");
                Check(incorrect < valid / 100, "Recovered samples must agree with the known ten-metre plane");

                texture.Erase(new SKColor(127, 127, 127));
                foreach (var photo in project.Images)
                {
                    images.Save(texture, store.PathFor(photo.Source), SKEncodedImageFormat.Png);
                }
                var blank = estimator.Estimate(store, project, scene, scene.Cameras[0], default, 2);
                Check(blank.Depth.All(depth => depth == 0), "Recovery must not invent depth for a textureless image");
            }
            finally
            {
                Directory.Delete(store.Root, true);
            }
        });
    }

    private void SetGray(SKBitmap bitmap, int x, int y, int value)
    {
        bitmap.SetPixel(x, y, new SKColor((byte)value, (byte)value, (byte)value));
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
