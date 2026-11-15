using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Fusion;

internal static class FusionGridBuilder
{
    public static FusionGrid Build(IReadOnlyList<DepthView> views, DenseCloudData cloud,
        Settings settings, FusionResourcePlan resources, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var detail = settings.FusionDetailMultiplier;
        var invalidDetail = !double.IsFinite(detail) || detail < .25 || detail > 16;
        if (invalidDetail)
        {
            throw new ArgumentOutOfRangeException(nameof(detail));
        }
        if (!double.IsFinite(settings.CloudDepthTolerance) || settings.CloudDepthTolerance <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings));
        }
        if (resources.MaximumGridSamples < 64)
        {
            throw new ArgumentOutOfRangeException(nameof(resources));
        }
        var footprint = FusionFootprint.Measure(views, cloud, cancellation);
        var byId = views.ToDictionary(view => view.Map.ImageId, StringComparer.Ordinal);
        var sources = cloud.ImageIds.Select(id => byId.GetValueOrDefault(id)).ToArray();
        var requested = footprint * .8 / Math.Sqrt(detail);
        var voxel = requested;
        for (var pass = 0; pass < 64; pass++)
        {
            cancellation.ThrowIfCancellationRequested();
            var candidates = Candidates(cloud, sources, settings.CloudDepthTolerance, voxel, resources.MaximumGridSamples, cancellation);
            if (candidates != null)
            {
                var keys = candidates.ToArray();
                Array.Sort(keys, Compare);
                return new FusionGrid { PixelFootprint = footprint, RequestedVoxelSize = requested, VoxelSize = voxel, Keys = keys };
            }
            // Count actual grid samples, including the extraction halo, instead
            // of predicting triangles. Never truncate coverage to fit memory.
            voxel *= Math.Sqrt(2);
        }
        throw new EchoException("Cannot fit the observed fusion volume within the available memory budget.", 5);
    }

    private static HashSet<Grid>? Candidates(DenseCloudData cloud, DepthView?[] sources, double relativeTolerance,
        double voxel, int limit, CancellationToken cancellation)
    {
        var occupied = new HashSet<Grid>();
        for (var index = 0; index < cloud.Samples.Count; index++)
        {
            if (index % 4096 == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }
            var point = cloud.Samples[index];
            var finite = float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
            if (!finite)
            {
                throw new EchoException("The dense cloud contains a non-finite position.", 5);
            }
            var validSource = point.View >= 0 && point.View < sources.Length;
            if (!validSource || sources[point.View] is not { } view)
            {
                throw new EchoException("A dense cloud sample has no source depth view.", 5);
            }
            var world = new V3(point.X, point.Y, point.Z);
            var ray = world - view.Centre;
            var cameraDepth = CameraGeometry.Rotate(view.Inverse, ray).Z;
            var invalidRay = !(cameraDepth > 0) || !double.IsFinite(cameraDepth)
                || !(view.Focal > 0) || !double.IsFinite(view.Focal);
            if (invalidRay)
            {
                throw new EchoException("A dense cloud sample has an invalid source-camera ray.", 5);
            }
            var uncertainty = FusionDepthUncertainty.Radius(cameraDepth, view.Focal, relativeTolerance);
            var extent = ray * (uncertainty / cameraDepth);
            var longestAxis = Math.Max(Math.Abs(extent.X), Math.Max(Math.Abs(extent.Y), Math.Abs(extent.Z)));
            var steps = Math.Ceiling(longestAxis / voxel);
            if (steps > limit)
            {
                return null;
            }
            var stepCount = Math.Max(1, (int)steps);
            var step = extent / stepCount;
            // A finer lattice must still span the accepted depth uncertainty.
            // Otherwise two agreeing observations can leave a candidate-free
            // crack between their surfaces. Extend along each measured ray;
            // the fixed spatial halo below handles extraction and roundoff.
            for (var offset = -stepCount; offset <= stepCount; offset++)
            {
                if (offset % 4096 == 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                }
                var sample = world + step * offset;
                occupied.Add(new Grid(Coordinate(sample.X, voxel), Coordinate(sample.Y, voxel), Coordinate(sample.Z, voxel)));
                if (occupied.Count > limit)
                {
                    return null;
                }
            }
        }
        var candidates = new HashSet<Grid>();
        foreach (var cell in occupied)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var z = -1; z <= 2; z++)
            {
                for (var y = -1; y <= 2; y++)
                {
                    for (var x = -1; x <= 2; x++)
                    {
                        candidates.Add(new Grid(cell.X + x, cell.Y + y, cell.Z + z));
                        if (candidates.Count > limit)
                        {
                            return null;
                        }
                    }
                }
            }
        }
        return candidates;
    }

    private static int Coordinate(double value, double voxel)
    {
        var coordinate = Math.Floor(value / voxel);
        var outsideRange = !double.IsFinite(coordinate) || coordinate < int.MinValue + 2.0 || coordinate > int.MaxValue - 2.0;
        if (outsideRange)
        {
            throw new EchoException("Fusion coordinates exceed the supported grid range.", 5);
        }
        return (int)coordinate;
    }

    private static int Compare(Grid a, Grid b)
    {
        var order = a.X.CompareTo(b.X);
        if (order == 0)
        {
            order = a.Y.CompareTo(b.Y);
        }
        return order != 0 ? order : a.Z.CompareTo(b.Z);
    }
}
