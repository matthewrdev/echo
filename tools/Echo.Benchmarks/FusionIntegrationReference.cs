using Echo.Fusion;
using Echo.Geometry;
using Echo.Models;

namespace Echo.Benchmarks;

// Benchmark oracle only: reproduces the previous per-voxel/per-view traversal.
// VolumeSample is now a value type, so allocation figures are not an old-build baseline.
internal static class FusionIntegrationReference
{
    public static IList<VolumeSample> Integrate(Grid[] cells, IReadOnlyList<DepthView> views, double voxel, double tolerance)
    {
        var samples = new VolumeSample[cells.Length];
        Parallel.For(0, cells.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, index =>
        {
            var key = cells[index];
            var world = new V3(key.X * voxel, key.Y * voxel, key.Z * voxel);
            var value = new FusionAccumulator();
            foreach (var view in views)
            {
                value.Add(view.Observe(world, tolerance), view.Focal, tolerance, voxel);
            }
            if (value.Support >= 2)
            {
                samples[index] = new VolumeSample
                {
                    X = key.X,
                    Y = key.Y,
                    Z = key.Z,
                    Distance = (float)(value.Sum / value.Weight),
                    Views = value.Support
                };
            }
        });
        return samples.Where(sample => sample.Views >= 2).ToArray();
    }
}
