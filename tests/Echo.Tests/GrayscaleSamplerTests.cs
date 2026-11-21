using Echo.Imaging;
using SkiaSharp;

namespace Echo.Tests;

public sealed class GrayscaleSamplerTests
{
    private readonly IRasterImageService images;

    public GrayscaleSamplerTests(IRasterImageService images)
    {
        this.images = images;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Span bilinear sampling preserves scalar float results and image boundaries exactly", () =>
        {
            var pixels = new float[173 * 117];
            var random = new Random(924);
            for (var index = 0; index < pixels.Length; index++)
            {
                pixels[index] = (float)random.NextDouble();
            }
            var sampler = new GrayscaleSampler(pixels, 173, 117);
            for (var index = 0; index < 10000; index++)
            {
                var x = random.NextDouble() * 175 - 1;
                var y = random.NextDouble() * 119 - 1;
                var expected = ReferenceSample(pixels, 173, 117, x, y);
                Check(sampler.Sample(x, y).Equals(expected), "Direct sampling must preserve every float result");
                Check(images.Sample(pixels, 173, 117, x, y).Equals(expected), "The image service uses the same sampler");
            }
            Check(sampler.Sample(0, 0) == pixels[0], "Top-left pixel is valid");
            Check(float.IsNaN(sampler.Sample(172, 0)), "Bilinear sampling requires the right neighbour");
            Check(float.IsNaN(sampler.Sample(0, 116)), "Bilinear sampling requires the lower neighbour");
        });

        tests.Run("Reusable grayscale destinations preserve pixels and leave unused pooled capacity untouched", () =>
        {
            using var source = new SKBitmap(173, 117);
            var random = new Random(242);
            var colours = new SKColor[source.Width * source.Height];
            for (var index = 0; index < colours.Length; index++)
            {
                colours[index] = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
            }
            source.Pixels = colours;
            var buffer = new float[32768];
            foreach (var width in new[] { 32, 80, 173, 256, 32 })
            {
                Array.Fill(buffer, -77);
                var expected = images.ResizeGray(source, width);
                var actual = images.ResizeGray(source, width, buffer);
                var dimensions = images.ResizeDimensions(source.Width, source.Height, width);
                Check(actual.Width == dimensions.Width && actual.Height == dimensions.Height, "Allocation and resize dimensions agree");
                Check(ReferenceEquals(actual.Pixels, buffer), "Resize must write into the borrowed buffer");
                Check(buffer.AsSpan(0, expected.Pixels.Length).SequenceEqual(expected.Pixels), "Reusing storage must preserve all grayscale values");
                Check(buffer[expected.Pixels.Length] == -77, "Unused pooled capacity is not part of the image");
            }
            try
            {
                images.ResizeGray(source, 80, new float[1]);
                throw new InvalidOperationException("Undersized output must be rejected");
            }
            catch (ArgumentException)
            {
            }
        });
    }

    private float ReferenceSample(float[] pixels, int width, int height, double x, double y)
    {
        var outsideHorizontal = x < 0 || x >= width - 1;
        var outsideVertical = y < 0 || y >= height - 1;
        if (outsideHorizontal || outsideVertical)
        {
            return float.NaN;
        }
        var ix = (int)x;
        var iy = (int)y;
        var dx = (float)(x - ix);
        var dy = (float)(y - iy);
        return pixels[iy * width + ix] * (1 - dx) * (1 - dy) + pixels[iy * width + ix + 1] * dx * (1 - dy)
            + pixels[(iy + 1) * width + ix] * (1 - dx) * dy + pixels[(iy + 1) * width + ix + 1] * dx * dy;
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
