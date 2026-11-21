using Echo.Models;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Fusion;

internal static class FusionGridBuilder
{
    public static FusionGrid Build(IReadOnlyList<DepthView> views, DenseCloudData cloud,
        Settings settings, FusionResourcePlan resources, CancellationToken cancellation, Action<string>? progress = null, WorkBuffers? work = null,
        Action<OperationProgress>? operationProgress = null)
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
        var invalidVoxelSize = !double.IsFinite(settings.FusionVoxelSize)
            || settings.FusionVoxelSize != 0 && settings.FusionVoxelSize < Settings.MinimumFusionVoxelSize;
        if (invalidVoxelSize)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Target voxel size must be Auto or at least 2.5 cm.");
        }
        if (resources.MaximumGridSamples < 64)
        {
            throw new ArgumentOutOfRangeException(nameof(resources));
        }
        var footprint = FusionFootprint.Measure(views, cloud, cancellation);
        var byId = views.ToDictionary(view => view.Map.ImageId, StringComparer.Ordinal);
        var sources = cloud.ImageIds.Select(id => byId.GetValueOrDefault(id)).ToArray();
        var requested = settings.FusionVoxelSize > 0 ? settings.FusionVoxelSize
            : Math.Max(Settings.MinimumFusionVoxelSize, footprint * .8 / Math.Sqrt(detail));
        var voxel = requested;
        // Automatic resolution plans a bounded workload. Explicit spacing in
        // paged mode may use disk, but must still fit downstream record indices.
        var automatic = settings.FusionVoxelSize == 0;
        var maximumSamples = work == null || automatic ? resources.MaximumGridSamples : int.MaxValue;
        var allowCoarser = automatic || settings.FusionAllowCoarserResolution;
        var status = new FusionPlanningProgress(operationProgress, settings.FusionIslandMode != IslandTrimMode.Off);
        var axis = FusionGridAxes.Select(cloud, sources, cancellation);
        for (var pass = 0; pass < 64; pass++)
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Invoke(work == null
                ? $"Planning fusion at {voxel:G6} m voxels within {resources.MemoryBudgetBytes / 1073741824.0:G4} GiB ({resources.MaximumGridSamples:N0} grid samples)…"
                : $"Planning fusion at {voxel:G6} m voxels using parallel batches and sequential scratch storage…");
            var options = FusionGridPlanning.Resolve(resources, cloud.Samples.Count) with
            {
                Axis = axis
            };
            progress?.Invoke($"Building fusion coverage with {options.Workers} CPU workers and bounded per-worker batches; duplicate cells are merged before integration.");
            var keys = FusionCandidatePlanner.Build(cloud, sources, settings.CloudDepthTolerance, voxel,
                maximumSamples, options, cancellation, work, status);
            if (keys != null)
            {
                return new FusionGrid { PixelFootprint = footprint, RequestedVoxelSize = requested, VoxelSize = voxel, Keys = keys };
            }
            if (!allowCoarser)
            {
                if (work != null)
                {
                    throw new EchoException($"Fusion at the requested {requested * 100:G4} cm exceeds the supported volume limit ({maximumSamples:N0} voxels). Resolution was not reduced. Choose a larger target voxel size or enable coarser resolution before running fusion again.", 5);
                }
                throw new EchoException($"Fusion at the requested {requested:G6} m voxel size exceeds the {resources.MemoryBudgetBytes / 1073741824.0:G4} GiB working-memory budget ({maximumSamples:N0} grid samples). Resolution was not reduced. Increase FusionMemoryBudgetMb, choose a larger FusionVoxelSize, or explicitly enable FusionAllowCoarserResolution.", 5);
            }
            // Count actual grid samples, including the extraction halo, instead
            // of predicting triangles. Never truncate coverage to fit memory.
            voxel *= 2;
            progress?.Invoke(automatic
                ? $"Automatic fusion resolution adjusted to {voxel * 100:G4} cm to fit {maximumSamples:N0} grid samples. Set a target voxel size to choose an exact resolution."
                : $"Requested fusion resolution does not fit. Coarser resolution is enabled: retrying at {voxel:G6} m voxels.");
        }
        throw new EchoException("Cannot fit the observed fusion volume within the available memory budget.", 5);
    }

}
