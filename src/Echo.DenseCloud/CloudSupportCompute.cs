using Echo.Compute;
using Echo.Contracts;
using Echo.Geometry;

namespace Echo.DenseCloud;

internal static class CloudSupportCompute
{
    public static int[]? TryCalculate(IComputeService? compute, DepthView reference, DepthView[] neighbours,
        Settings settings, CancellationToken cancellation)
    {
        try
        {
            using var session = DepthCompute.Open(compute, settings.ComputeBackend, new[] { reference }.Concat(neighbours).ToArray(), reference.Centre, cancellation);
            if (session == null)
            {
                return null;
            }
            var sine = Math.Sin(settings.CloudMinimumParallaxDegrees * Math.PI / 180);
            session.Upload<float>(2, [(float)settings.CloudDepthTolerance, (float)settings.CloudReprojectionTolerancePixels, (float)(sine * sine)]);
            var support = new int[reference.Map.SampleCount];
            session.Allocate(3, checked(support.Length * sizeof(int)));
            session.Dispatch("cloud_support", support.Length, [neighbours.Length + 1]);
            session.Read<int>(3, support);
            return support;
        }
        catch (ComputeException) when (settings.ComputeBackend == ComputeBackend.Auto)
        {
            return null;
        }
    }
}
