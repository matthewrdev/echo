using Echo.Compute;

namespace Echo.Features;

internal static class GaussianCompute
{
    public static void Apply(IComputeSession session, ReadOnlySpan<float> source, Span<float> destination,
        int width, int height, double sigma, CancellationToken cancellation)
    {
        var radius = (int)Math.Ceiling(4 * sigma);
        Span<float> weights = stackalloc float[2 * radius + 1];
        double total = 0;
        for (var k = -radius; k <= radius; k++)
        {
            total += Math.Exp(-k * k / (2 * sigma * sigma));
        }
        for (var k = -radius; k <= radius; k++)
        {
            weights[k + radius] = (float)(Math.Exp(-k * k / (2 * sigma * sigma)) / total);
        }
        cancellation.ThrowIfCancellationRequested();
        session.Upload<float>(0, source);
        session.Allocate(1, checked(width * height * sizeof(float)));
        session.Allocate(2, checked(width * height * sizeof(float)));
        session.Upload<float>(3, weights);
        session.Dispatch("gaussian_horizontal", width * height, [width, height, radius]);
        session.Dispatch("gaussian_vertical", width * height, [width, height, radius]);
        session.Read<float>(2, destination);
    }
}
