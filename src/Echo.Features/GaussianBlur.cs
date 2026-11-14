using System.Buffers;

namespace Echo.Features;

public static class GaussianBlur
{
    public static void Apply(ReadOnlySpan<float> source, Span<float> destination, int width, int height, double sigma, CancellationToken cancellation)
    {
        var radius = (int)Math.Ceiling(4 * sigma);
        Span<double> kernel = stackalloc double[2 * radius + 1];
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

        var buffer = ArrayPool<float>.Shared.Rent(width * height);
        try
        {
            var intermediate = buffer.AsSpan(0, width * height);
            var interiorStart = Math.Min(radius, width);
            var interiorEnd = Math.Max(interiorStart, width - radius);
            for (var y = 0; y < height; y++)
            {
                cancellation.ThrowIfCancellationRequested();
                var row = y * width;
                for (var x = 0; x < interiorStart; x++)
                {
                    double value = 0;
                    for (var offset = -radius; offset <= radius; offset++)
                    {
                        value += source[row + Math.Clamp(x + offset, 0, width - 1)] * kernel[offset + radius];
                    }
                    intermediate[row + x] = (float)value;
                }
                for (var x = interiorStart; x < interiorEnd; x++)
                {
                    double value = 0;
                    for (var offset = -radius; offset <= radius; offset++)
                    {
                        value += source[row + x + offset] * kernel[offset + radius];
                    }
                    intermediate[row + x] = (float)value;
                }
                for (var x = interiorEnd; x < width; x++)
                {
                    double value = 0;
                    for (var offset = -radius; offset <= radius; offset++)
                    {
                        value += source[row + Math.Clamp(x + offset, 0, width - 1)] * kernel[offset + radius];
                    }
                    intermediate[row + x] = (float)value;
                }
            }
            Span<int> rows = stackalloc int[kernel.Length];
            for (var y = 0; y < height; y++)
            {
                cancellation.ThrowIfCancellationRequested();
                // Every pixel in this row uses the same source rows. Resolve border
                // clamping once, retaining the original tap and accumulation order.
                for (var offset = -radius; offset <= radius; offset++)
                {
                    rows[offset + radius] = Math.Clamp(y + offset, 0, height - 1) * width;
                }
                for (var x = 0; x < width; x++)
                {
                    double value = 0;
                    for (var offset = -radius; offset <= radius; offset++)
                    {
                        value += intermediate[rows[offset + radius] + x] * kernel[offset + radius];
                    }
                    destination[y * width + x] = (float)value;
                }
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(buffer);
        }
    }
}
