using Echo.Models;
using Echo.Features;
using Echo.Infrastructure;
using Echo.Matching;

namespace Echo.Tests;

public sealed class ScaleSpaceFeatureTests
{
    private readonly IFeatureMatcher matcher;
    private readonly IFileOperations files;

    public ScaleSpaceFeatureTests(IFeatureMatcher matcher, IFileOperations files)
    {
        this.matcher = matcher;
        this.files = files;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Scale-space features match a known rotation and scale with independent pixel truth", () =>
        {
            const int width = 320;
            var source = Pattern(width);
            var transformed = new float[source.Length];
            var angle = .43;
            var scale = .68;
            var cosine = Math.Cos(angle);
            var sine = Math.Sin(angle);
            for (var y = 0; y < width; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var dx = (x - width * .5) / scale;
                    var dy = (y - width * .5) / scale;
                    transformed[y * width + x] = Sample(source, width, cosine * dx + sine * dy + width * .5, -sine * dx + cosine * dy + width * .5);
                }
            }
            var left = new FeatureSet { Features = ScaleSpaceFeatures.Detect(source, width, width, 600, default) };
            var right = new FeatureSet { Features = ScaleSpaceFeatures.Detect(transformed, width, width, 600, default) };
            var pair = matcher.Match(left, right, new Settings(), default);
            var correct = 0;
            foreach (var match in pair.Matches)
            {
                var dx = match.A[0] - width * .5;
                var dy = match.A[1] - width * .5;
                var targetX = (cosine * dx - sine * dy) * scale + width * .5;
                var targetY = (sine * dx + cosine * dy) * scale + width * .5;
                if (Math.Sqrt(Math.Pow(targetX - match.B[0], 2) + Math.Pow(targetY - match.B[1], 2)) < 2)
                {
                    correct++;
                }
            }
            Check(correct >= 25 && correct >= pair.Matches.Count * .9,
                $"Need at least 25 correct and 90% precision; got {correct}/{pair.Matches.Count}, features {left.Features.Count}/{right.Features.Count}");
            var hash = files.Hash(left);
            Parallel.For(0, 3, index =>
            {
                ScaleSpaceFeatures.Detect(new float[64 * 64], 64, 64, 32, default);
                Check(files.Hash(new FeatureSet { Features = ScaleSpaceFeatures.Detect(source, width, width, 600, default) }) == hash, "Concurrent pools must produce identical descriptors");
            });
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try
            {
                ScaleSpaceFeatures.Detect(source, width, width, 600, cancelled.Token);
                throw new InvalidOperationException("Cancellation must abort extraction");
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private float[] Pattern(int width)
    {
        var pixels = new float[width * width];
        Array.Fill(pixels, .5f);
        var random = new Random(701);
        for (var item = 0; item < 450; item++)
        {
            var centreX = random.Next(15, width - 15);
            var centreY = random.Next(15, width - 15);
            var sigmaX = random.Next(2, 9);
            var sigmaY = random.Next(2, 9);
            var amplitude = random.NextDouble() * .8 - .4;
            for (var y = Math.Max(0, centreY - sigmaY * 3); y < Math.Min(width, centreY + sigmaY * 3); y++)
            {
                for (var x = Math.Max(0, centreX - sigmaX * 3); x < Math.Min(width, centreX + sigmaX * 3); x++)
                {
                    var dx = (double)(x - centreX) / sigmaX;
                    var dy = (double)(y - centreY) / sigmaY;
                    pixels[y * width + x] += (float)(amplitude * Math.Exp(-.5 * (dx * dx + dy * dy)));
                }
            }
        }
        return pixels;
    }

    private float Sample(float[] pixels, int width, double x, double y)
    {
        var outside = x < 0 || y < 0 || x >= width - 1 || y >= width - 1;
        if (outside)
        {
            return .5f;
        }
        var column = (int)x;
        var row = (int)y;
        var dx = x - column;
        var dy = y - row;
        var offset = row * width + column;
        return (float)((pixels[offset] * (1 - dx) + pixels[offset + 1] * dx) * (1 - dy)
            + (pixels[offset + width] * (1 - dx) + pixels[offset + width + 1] * dx) * dy);
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
