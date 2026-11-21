using Echo.Models;
using Echo.Geometry;

namespace Echo.Fusion;

internal static class FusionFootprint
{
    public static double Measure(IReadOnlyList<DepthView> views, DenseCloudData cloud, CancellationToken cancellation)
    {
        var byId = views.ToDictionary(view => view.Map.ImageId, StringComparer.Ordinal);
        var sources = cloud.ImageIds.Select(id => byId.GetValueOrDefault(id)).ToArray();
        var footprints = new List<double>();
        // Bounded, deterministic sampling of the geometrically verified cloud.
        // Rejected raw depth pixels do not set the resolution.
        var stride = Math.Max(1, (cloud.Samples.Count + 65535L) / 65536);
        for (long index = 0; index < cloud.Samples.Count; index += stride)
        {
            cancellation.ThrowIfCancellationRequested();
            var sample = cloud.Samples[(int)index];
            var validSource = sample.View >= 0 && sample.View < sources.Length;
            if (!validSource || sources[sample.View] is not { } view)
            {
                continue;
            }
            var local = CameraGeometry.Rotate(view.Inverse, new V3(sample.X, sample.Y, sample.Z) - view.Centre);
            var footprint = local.Z / view.Focal;
            var validFootprint = local.Z > 0 && footprint > 0 && double.IsFinite(footprint);
            if (validFootprint)
            {
                footprints.Add(footprint);
            }
        }
        if (footprints.Count == 0)
        {
            throw new EchoException("No verified surface samples have a valid source-camera footprint.", 5);
        }
        footprints.Sort();
        // The lower quartile keeps closer, more detailed observations influential
        // without allowing a single near-camera outlier to dictate the grid.
        return footprints[(footprints.Count - 1) / 4];
    }
}
