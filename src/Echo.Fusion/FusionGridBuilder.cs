using Echo.Contracts;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Fusion;

internal static class FusionGridBuilder
{
    public static FusionGrid Build(IReadOnlyList<DepthView> views, DenseCloudData cloud,
        Settings settings, FusionResourcePlan resources, CancellationToken cancellation, Action<string>? progress = null, WorkBuffers? work = null)
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
        if (!double.IsFinite(settings.FusionVoxelSize) || settings.FusionVoxelSize < 0)
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
        var requested = settings.FusionVoxelSize > 0 ? settings.FusionVoxelSize : footprint * .8 / Math.Sqrt(detail);
        var voxel = requested;
        for (var pass = 0; pass < 64; pass++)
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Invoke(work == null
                ? $"Planning fusion at {voxel:G6} m voxels within {resources.MemoryBudgetBytes / 1073741824.0:G4} GiB ({resources.MaximumGridSamples:N0} grid samples)…"
                : $"Planning fusion at {voxel:G6} m voxels with a bounded cache and disk-backed grid…");
            var candidates = Candidates(cloud, sources, settings.CloudDepthTolerance, voxel, work == null ? resources.MaximumGridSamples : int.MaxValue, cancellation, work);
            if (candidates != null)
            {
                IList<Grid> keys;
                if (candidates is IOrderedRecordKeys<Grid> disk)
                {
                    var sorted = work!.List(new GridRecordCodec());
                    foreach (var key in disk.OrderedKeys())
                    {
                        cancellation.ThrowIfCancellationRequested();
                        sorted.Add(key);
                    }
                    keys = sorted;
                }
                else
                {
                    var sorted = candidates.Keys.ToArray();
                    Array.Sort(sorted, Compare);
                    keys = sorted;
                }
                return new FusionGrid { PixelFootprint = footprint, RequestedVoxelSize = requested, VoxelSize = voxel, Keys = keys };
            }
            if (!settings.FusionAllowCoarserResolution)
            {
                throw new EchoException($"Fusion at the requested {requested:G6} m voxel size exceeds the {resources.MemoryBudgetBytes / 1073741824.0:G4} GiB working-memory budget ({resources.MaximumGridSamples:N0} grid samples). Resolution was not reduced. Increase FusionMemoryBudgetMb, choose a larger FusionVoxelSize, or explicitly enable FusionAllowCoarserResolution.", 5);
            }
            // Count actual grid samples, including the extraction halo, instead
            // of predicting triangles. Never truncate coverage to fit memory.
            voxel *= Math.Sqrt(2);
            progress?.Invoke($"Requested fusion resolution does not fit. Coarser resolution is enabled: retrying at {voxel:G6} m voxels.");
        }
        throw new EchoException("Cannot fit the observed fusion volume within the available memory budget.", 5);
    }

    private static IDictionary<Grid, int>? Candidates(DenseCloudData cloud, DepthView?[] sources, double relativeTolerance,
        double voxel, int limit, CancellationToken cancellation, WorkBuffers? work)
    {
        var occupied = work?.Map(new GridRecordCodec(), new IntRecordCodec()) ?? new Dictionary<Grid, int>();
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
                occupied.TryAdd(new Grid(Coordinate(sample.X, voxel), Coordinate(sample.Y, voxel), Coordinate(sample.Z, voxel)), 0);
                if (occupied.Count > limit)
                {
                    return null;
                }
            }
        }
        var candidates = work?.Map(new GridRecordCodec(), new IntRecordCodec()) ?? new Dictionary<Grid, int>();
        foreach (var cell in occupied.Keys)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var z = -1; z <= 2; z++)
            {
                for (var y = -1; y <= 2; y++)
                {
                    for (var x = -1; x <= 2; x++)
                    {
                        candidates.TryAdd(new Grid(cell.X + x, cell.Y + y, cell.Z + z), 0);
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
