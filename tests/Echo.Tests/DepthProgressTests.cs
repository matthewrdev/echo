using Echo.Models;
using Echo.Depth;
using Echo.Imaging;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Tests;

public sealed class DepthProgressTests
{
    private readonly IDepthEstimator estimator;
    private readonly IProjectStoreFactory stores;
    private readonly IRasterImageService images;

    public DepthProgressTests(IDepthEstimator estimator, IProjectStoreFactory stores, IRasterImageService images)
    {
        this.estimator = estimator;
        this.stores = stores;
        this.images = images;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Depth reports refinement rows and honours cancellation without changing numerical output", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "echo-depth-progress-" + Guid.NewGuid().ToString("N"));
            var store = stores.Open(root);
            store.Init();
            try
            {
                using var bitmap = new SKBitmap(192, 128);
                var random = new Random(18);
                for (var y = 0; y < bitmap.Height; y++)
                {
                    for (var x = 0; x < bitmap.Width; x++)
                    {
                        var shade = (byte)random.Next(256);
                        bitmap.SetPixel(x, y, new SKColor(shade, shade, shade));
                    }
                }

                images.Save(bitmap, store.PathFor("image.png"), SKEncodedImageFormat.Png);
                var project = new ProjectData
                {
                    Settings = new Settings { DepthWidth = 192, DepthPlanes = 8, DepthNeighbours = 1 },
                    Images =
                    [
                        new Photo { Id = "left", Source = "image.png", Width = 192, Height = 128, FocalPixels = 150 },
                        new Photo { Id = "right", Source = "image.png", Width = 192, Height = 128, FocalPixels = 150 }
                    ]
                };
                var scene = new ReconstructionScene
                {
                    Cameras = [new CameraPose { ImageId = "left" }, new CameraPose { ImageId = "right", Centre = [1, 0, 0] }]
                };
                var messages = new List<string>();
                var reference = estimator.Estimate(store, project, scene, scene.Cameras[0], default, 1);
                var reported = estimator.Estimate(store, project, scene, scene.Cameras[0], default, 2, messages.Add);
                Check(reference.Depth.AsSpan().SequenceEqual(reported.Depth), "Progress and row scheduling must preserve depths exactly");
                Check(reference.Confidence.AsSpan().SequenceEqual(reported.Confidence), "Progress must preserve confidence");
                Check(messages.Exists(message => message.Contains("level 2/2") && message.Contains("rows")), "Report rows inside each refinement level");
                using var cancellation = new CancellationTokenSource();
                try
                {
                    estimator.Estimate(store, project, scene, scene.Cameras[0], cancellation.Token, 2, message =>
                    {
                        if (message.Contains("rows"))
                        {
                            cancellation.Cancel();
                        }
                    });
                    throw new InvalidOperationException("Cancellation must stop the current image");
                }
                catch (OperationCanceledException)
                {
                }
            }
            finally
            {
                Directory.Delete(root, true);
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
