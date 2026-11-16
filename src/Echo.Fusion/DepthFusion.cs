using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Compute;
using Echo.Storage;
using Echo.Geometry;

namespace Echo.Fusion;

[Export(typeof(IDepthFusion)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class DepthFusion : IDepthFusion
{
    private readonly IComputeService? compute;

    [ImportingConstructor]
    public DepthFusion([Import(AllowDefault = true)] IComputeService? compute = null)
    {
        this.compute = compute;
    }

    public FusedVolume Fuse(ProjectData project,
        ReconstructionScene scene,
        IReadOnlyList<DepthMap> maps,
        DenseCloudData cloud,
        FusionResourcePlan resources,
        CancellationToken cancellation, Action<string>? progress = null, WorkBuffers? work = null, IFusionChunkStore? chunks = null, Action<OperationProgress>? operationProgress = null, Action<FusionPreview>? preview = null)
    {
        var photos = new Dictionary<string, Photo>(project.Images.Count, StringComparer.Ordinal);
        var cameras = new Dictionary<string, CameraPose>(scene.Cameras.Count, StringComparer.Ordinal);
        foreach (var photo in project.Images)
        {
            photos.Add(photo.Id, photo);
        }
        foreach (var camera in scene.Cameras)
        {
            cameras.Add(camera.ImageId, camera);
        }
        var views = new DepthView[maps.Count];
        for (var index = 0; index < maps.Count; index++)
        {
            var source = maps[index];
            var map = DepthExclusions.Apply(photos[source.ImageId], cameras[source.ImageId], source, project.Settings.ExclusionRegions);
            views[index] = new DepthView(photos[map.ImageId], cameras[map.ImageId], map);
        }
        cloud = FusionExclusions.Filter(cloud, project.Settings.ExclusionRegions, cancellation, work);
        var grid = FusionGridBuilder.Build(views, cloud, project.Settings, resources, cancellation, progress, work, operationProgress);
        progress?.Invoke($"Fusion resolution: requested {grid.RequestedVoxelSize:G6} m; using {grid.VoxelSize:G6} m across {grid.Keys.Count:N0} grid samples.");
        var voxel = grid.VoxelSize;
        var keys = grid.Keys;
        var result = new FusedVolume
        {
            Samples = work?.List(new VolumeRecordCodec()) ?? [],
            VoxelSize = voxel,
            InputSamples = cloud.InputSamples,
            ConsistentSamples = cloud.Samples.Count,
            Resolution = new FusionResolutionReport
            {
                PixelFootprint = grid.PixelFootprint,
                RequestedVoxelSize = grid.RequestedVoxelSize,
                MemoryBudgetBytes = resources.MemoryBudgetBytes,
                MaximumGridSamples = resources.MaximumGridSamples,
                CandidateSamples = keys.Count,
                MemoryLimited = voxel > grid.RequestedVoxelSize * 1.001
            }
        };
        var cleanup = project.Settings.FusionIslandMode != IslandTrimMode.Off;
        var plan = FusionIntegrationPlanning.Resolve(resources.MemoryBudgetBytes, compute?.MemoryBudgetBytes ?? 0,
            keys.Count, views.Sum(view => (long)view.Map.SampleCount));
        operationProgress?.Invoke(FusionProgressPlan.Create(FusionPhase.DepthPreparation, cleanup,
            "Preparing depth maps for integration…", 0, views.Length, "depth maps"));
        using var integrator = new FusionBatchIntegrator(views, voxel, project.Settings.CloudDepthTolerance, plan,
            compute, plan.SelectBackend(project.Settings.ComputeBackend), cancellation, progress,
            (done, total) => operationProgress?.Invoke(FusionProgressPlan.Create(FusionPhase.DepthPreparation,
                cleanup, $"Loading resident depth maps: {done:N0}/{total:N0}", done, total, "depth maps")));
        operationProgress?.Invoke(FusionProgressPlan.Create(FusionPhase.DepthPreparation, cleanup,
            "Depth integration ready.", views.Length, views.Length, "depth maps"));
        var previewSamples = preview == null ? null : new FusionPreviewSampler();
        var appender = new FusionVolumeAppender(result.Samples, previewSamples, project.Settings.ExclusionRegions, voxel);
        long lastPreview = 0;
        using var batches = new FusionChunkIntegrator(integrator, keys.OpenReader(),
            chunks == null ? integrator.Capacity : plan.CheckpointCapacity, cancellation);
        var integrationProgress = new FusionPlanningProgress(update =>
        {
            if (operationProgress != null)
            {
                operationProgress(update);
            }
            else
            {
                progress?.Invoke(update.Message);
            }
        }, cleanup);
        integrationProgress.Start(FusionPhase.Integration, keys.Count, "grid samples");
        for (long first = 0; first < keys.Count; first += batches.Capacity)
        {
            cancellation.ThrowIfCancellationRequested();
            var count = (int)Math.Min(batches.Capacity, keys.Count - first);
            var generated = false;
            IList<VolumeSample> Integrate()
            {
                generated = true;
                return batches.Integrate(count, done => integrationProgress.Advance(first + done));
            }
            var completed = chunks?.ReadOrBuild(first, count, Integrate) ?? Integrate();
            if (!generated)
            {
                batches.Skip(count);
            }
            appender.Append(completed);
            if (preview != null && Environment.TickCount64 - lastPreview >= 3000)
            {
                preview(previewSamples!.Snapshot(result));
                lastPreview = Environment.TickCount64;
            }
            integrationProgress.Advance(first + count);
        }
        progress?.Invoke($"Integrated {keys.Count:N0} grid samples; {result.Samples.Count:N0} retained.");
        if (result.Samples.Count < 100)
        {
            throw new EchoException("Too little jointly observed volume to reconstruct a surface.", 5);
        }

        return result;
    }
}
