using Echo.Features;

namespace Echo.Tests;

public sealed class GaussianBlurTests
{
    public GaussianBlurTests()
    {
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Gaussian interior and borders remain bit identical to the original filter", () =>
        {
            foreach (var width in new[] { 1, 2, 7, 16, 73 })
            {
                foreach (var height in new[] { 1, 3, 11, 48 })
                {
                    foreach (var sigma in new[] { .3, 1.6, 3.2, 5.1 })
                    {
                        var random = new Random(width * 100 + height);
                        var source = Enumerable.Range(0, width * height).Select(_ => (float)(random.NextDouble() * 4 - 2)).ToArray();
                        var expected = Reference(source, width, height, sigma);
                        var actual = new float[source.Length];
                        GaussianBlur.Apply(source, actual, width, height, sigma, default);
                        CheckBits(expected, actual);
                        // An aliased destination is supported by the intermediate plane.
                        GaussianBlur.Apply(source, source, width, height, sigma, default);
                        CheckBits(expected, source);
                    }
                }
            }
        });
        tests.Run("Gaussian cancellation and concurrent pooled buffers preserve output", () =>
        {
            var source = Enumerable.Range(0, 93 * 61).Select(i => (float)Math.Sin(i)).ToArray();
            var expected = Reference(source, 93, 61, 1.6);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try
            {
                GaussianBlur.Apply(source, new float[source.Length], 93, 61, 1.6, cancelled.Token);
                throw new InvalidOperationException("Expected cancellation");
            }
            catch (OperationCanceledException)
            {
            }

            Parallel.For(0, 8, _ =>
            {
                var actual = new float[source.Length];
                GaussianBlur.Apply(source, actual, 93, 61, 1.6, default);
                CheckBits(expected, actual);
            });
        });
    }

    private float[] Reference(float[] source, int width, int height, double sigma)
    {
        // Original scalar implementation: independent baseline for border addressing
        // and the exact double accumulation / float rounding at each pass.
        var radius = (int)Math.Ceiling(4 * sigma);
        var kernel = new double[2 * radius + 1];
        double total = 0;
        for (var offset = -radius; offset <= radius; offset++)
        {
            var weight = Math.Exp(-offset * offset / (2 * sigma * sigma));
            kernel[offset + radius] = weight;
            total += weight;
        }

        for (var index = 0; index < kernel.Length; index++)
        {
            kernel[index] /= total;
        }

        var intermediate = new float[source.Length];
        var destination = new float[source.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                double value = 0;
                for (var offset = -radius; offset <= radius; offset++)
                {
                    value += source[y * width + Math.Clamp(x + offset, 0, width - 1)] * kernel[offset + radius];
                }

                intermediate[y * width + x] = (float)value;
            }
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                double value = 0;
                for (var offset = -radius; offset <= radius; offset++)
                {
                    value += intermediate[Math.Clamp(y + offset, 0, height - 1) * width + x] * kernel[offset + radius];
                }

                destination[y * width + x] = (float)value;
            }
        }

        return destination;
    }

    private void CheckBits(float[] expected, float[] actual)
    {
        for (var index = 0; index < expected.Length; index++)
        {
            if (BitConverter.SingleToInt32Bits(expected[index]) != BitConverter.SingleToInt32Bits(actual[index]))
            {
                throw new InvalidOperationException($"Gaussian sample {index} differs: {expected[index]} / {actual[index]}");
            }
        }
    }
}
