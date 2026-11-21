using Echo.Depth;
using Echo.Features;

namespace Echo.Tests;

public sealed class StereoImageFilterTests
{
    public StereoImageFilterTests()
    {
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Rolling stereo image filter matches independent Gaussian filtering including narrow-image borders", () =>
        {
            var random = new Random(27);
            foreach (var height in new[] { 1, 2, 7, 31 })
            {
                const int width = 23;
                var source = Enumerable.Range(0, width * height).Select(_ => (float)random.NextDouble()).ToArray();
                var expected = new float[source.Length];
                GaussianBlur.Apply(source, expected, width, height, .7, default);
                StereoImageFilter.Apply(source, width, height, default);
                if (!source.AsSpan().SequenceEqual(expected))
                {
                    throw new InvalidOperationException("Rolling rows changed Gaussian samples or reused overwritten input");
                }
            }
        });
    }
}
