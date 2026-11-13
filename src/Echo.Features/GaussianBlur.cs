using System.Buffers;
using System.ComponentModel.Composition;

namespace Echo.Features;

[Export(typeof(IGaussianBlur)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class GaussianBlur : IGaussianBlur
{
    [ImportingConstructor]
    public GaussianBlur()
    {
    }

    public void Apply(ReadOnlySpan<float> source, Span<float> destination, int width, int height, double sigma, CancellationToken cancellation)
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
            for (var y = 0; y < height; y++)
            {
                cancellation.ThrowIfCancellationRequested();
                var row = y * width;
                for (var x = 0; x < width; x++)
                {
                    double value = 0;
                    for (var offset = -radius; offset <= radius; offset++)
                    {
                        value += source[row + Math.Clamp(x + offset, 0, width - 1)] * kernel[offset + radius];
                    }
                    intermediate[row + x] = (float)value;
                }
            }
            for (var y = 0; y < height; y++)
            {
                cancellation.ThrowIfCancellationRequested();
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
        }
        finally
        {
            ArrayPool<float>.Shared.Return(buffer);
        }
    }
}
