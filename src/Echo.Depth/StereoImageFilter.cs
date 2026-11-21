using System.Buffers;

namespace Echo.Depth;

internal static class StereoImageFilter
{
    public static void Apply(Span<float> pixels, int width, int height, CancellationToken cancellation)
    {
        // A subpixel Gaussian suppresses resampling noise before a wider stereo
        // patch is sampled. Seven rolling rows bound scratch space to O(width).
        Span<double> kernel = stackalloc double[7];
        double total = 0;
        for (var index = 0; index < kernel.Length; index++)
        {
            var distance = index - 3;
            kernel[index] = Math.Exp(-distance * distance / (2 * .7 * .7));
            total += kernel[index];
        }

        for (var index = 0; index < kernel.Length; index++)
        {
            kernel[index] /= total;
        }

        var buffer = ArrayPool<float>.Shared.Rent(checked(width * kernel.Length));
        try
        {
            for (var row = 0; row < kernel.Length; row++)
            {
                Horizontal(pixels.Slice(Math.Clamp(row - 3, 0, height - 1) * width, width), buffer.AsSpan(row * width, width), kernel);
            }

            for (var y = 0; y < height; y++)
            {
                cancellation.ThrowIfCancellationRequested();
                for (var x = 0; x < width; x++)
                {
                    double value = 0;
                    for (var row = 0; row < kernel.Length; row++)
                    {
                        value += buffer[((y + row) % kernel.Length) * width + x] * kernel[row];
                    }

                    pixels[y * width + x] = (float)value;
                }

                if (y + 1 < height)
                {
                    Horizontal(pixels.Slice(Math.Min(y + 4, height - 1) * width, width), buffer.AsSpan(y % kernel.Length * width, width), kernel);
                }
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(buffer);
        }
    }

    private static void Horizontal(ReadOnlySpan<float> source, Span<float> destination, ReadOnlySpan<double> kernel)
    {
        for (var x = 0; x < source.Length; x++)
        {
            double value = 0;
            for (var index = 0; index < kernel.Length; index++)
            {
                value += source[Math.Clamp(x + index - 3, 0, source.Length - 1)] * kernel[index];
            }

            destination[x] = (float)value;
        }
    }
}
