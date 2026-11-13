using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Features;
using Echo.Imaging;
using Echo.Infrastructure;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Tests;

[Export]
public sealed class FeatureExtractionTests
{
    private readonly IFeatureExtractor extractor;
    private readonly IRasterImageService images;
    private readonly IProjectStoreFactory stores;
    private readonly IFileOperations files;

    [ImportingConstructor]
    public FeatureExtractionTests(IFeatureExtractor extractor, IRasterImageService images, IProjectStoreFactory stores, IFileOperations files)
    {
        this.extractor = extractor;
        this.images = images;
        this.stores = stores;
        this.files = files;
    }

    internal void Run(TestSuite tests)
    {
        VerifyPattern(tests, "checker", 256, false, "641484772d55232f015de1def9d9419f91101b61238ab1956269942e4af5c4bd");
        VerifyPattern(tests, "noise", 173, true, "f04475b5df8c4cea7f84e8d3f76629ba8bcc5fd5b62c6fceb83a297406fcd7c2");
        VerifyPattern(tests, "rectangle", 256, true, "8cd4d6fcae792b27918412bb10766ed3e078999511efca171f6d85c1523e23a8");
    }

    private void VerifyPattern(TestSuite tests, string pattern, int width, bool detectLines, string expectedHash)
    {
        tests.Run($"Feature extraction preserves {pattern} output, ordering and concurrent buffer isolation", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "echo-features-" + Guid.NewGuid().ToString("N"));
            var store = stores.Open(root);
            store.Init();
            try
            {
                using var bitmap = MakePattern(pattern);
                images.Save(bitmap, store.PathFor("pattern.png"), SKEncodedImageFormat.Png);
                var photo = new Photo { Id = pattern, Source = "pattern.png", Width = bitmap.Width, Height = bitmap.Height };
                var settings = new Settings { FeatureWidth = width, FeatureLimit = 300, DetectLines = detectLines, LineMinimumLength = 12 };
                var actual = files.Hash(extractor.Extract(store, photo, settings, default));
                Check(actual == expectedHash, $"Baseline {pattern}: expected {expectedHash}, got {actual}");
                Parallel.For(0, 6, new ParallelOptions { MaxDegreeOfParallelism = 2 }, iteration =>
                {
                    // Alternate dimensions to exercise dirty pooled rows and borders.
                    var alternate = new Settings { FeatureWidth = 64 + iteration * 16, FeatureLimit = 32, DetectLines = true };
                    extractor.Extract(store, photo, alternate, default);
                    var repeated = files.Hash(extractor.Extract(store, photo, settings, default));
                    Check(repeated == expectedHash, "Each extraction owns its scratch storage");
                });
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                try
                {
                    extractor.Extract(store, photo, settings, cancelled.Token);
                    throw new InvalidOperationException("Cancelled extraction must not run");
                }
                catch (OperationCanceledException)
                {
                }
                Check(files.Hash(extractor.Extract(store, photo, settings, default)) == expectedHash, "Cancellation does not affect the next extraction");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        });
    }

    private SKBitmap MakePattern(string pattern)
    {
        var bitmap = new SKBitmap(256, 192, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var pixels = new SKColor[bitmap.Width * bitmap.Height];
        var random = new Random(428);
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var value = (byte)(((x / 16 + y / 16) % 2 == 0) ? 220 : 35);
                var color = new SKColor(value, value, value);
                if (pattern == "noise")
                {
                    color = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
                }
                else if (pattern == "rectangle")
                {
                    var insideX = x >= 60 && x < 200;
                    var insideY = y >= 30 && y < 160;
                    color = insideX && insideY ? new SKColor(200, 180, 160) : new SKColor(10, 20, 30);
                }
                pixels[y * bitmap.Width + x] = color;
            }
        }
        bitmap.Pixels = pixels;
        return bitmap;
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
