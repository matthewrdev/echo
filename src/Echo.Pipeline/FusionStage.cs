using Echo.Compute;
using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.DenseCloud;
using Echo.Fusion;
using Echo.Infrastructure;

namespace Echo.Pipeline;

[Export(typeof(IPipelineStage)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class FusionStage : IPipelineStage
{
    private readonly IFileOperations fileOperations;
    private readonly IJsonCodec jsonCodec;
    private readonly ICloudBuilder cloudBuilder;
    private readonly IDepthFusion depthFusion;
    private readonly IFusionResourcePlanner resourcePlanner;

    [ImportingConstructor]
    public FusionStage(IFileOperations fileOperations, IJsonCodec jsonCodec, ICloudBuilder cloudBuilder,
        IDepthFusion depthFusion, IFusionResourcePlanner resourcePlanner)
    {
        this.fileOperations = fileOperations;
        this.jsonCodec = jsonCodec;
        this.cloudBuilder = cloudBuilder;
        this.depthFusion = depthFusion;
        this.resourcePlanner = resourcePlanner;
    }

    public string Name => "fusion";
    public int Order => 5;

    public void Execute(RunContext context)
    {
        var project = context.Run.Snapshot;
        var store = context.Store;
        var cancellation = context.Cancellation;
        var alignment = context.Artifact("alignment");
        var scene = context.Scene();
        var depthUnits = context.DepthUnits();
        var cloudPath = context.Artifact("cloud");
        var resources = resourcePlanner.Plan(project.Settings);
        var inputs = new
        {
            cloudPath,
            depthUnits,
            alignment,
            maximumGridSamples = context.Paged ? 0 : resources.MaximumGridSamples,
            fusionVersion = 10,
            project.Settings.ComputeBackend,
            computeImplementation = context.ComputeImplementation(ComputeOperation.Fusion),
            project.Settings.CloudDepthTolerance,
            project.Settings.FusionDetailMultiplier,
            project.Settings.FusionVoxelSize,
            project.Settings.FusionAllowCoarserResolution,
            project.Settings.FusionIslandMode,
            project.Settings.FusionIslandMinimumSizePercent
        };
        var fusionPath = context.Build("fusion", inputs, dir =>
                {
                    var previews = new RunPreviewPublisher(context, fileOperations, RunPreviewStage.Fusion);
                    var maps = context.DepthMaps();
                    var cloud = cloudBuilder.Read(store.PathFor(cloudPath + "/cloud.ply"), jsonCodec.Read<DenseCloudReport>(store.PathFor(cloudPath + "/cloud.json")), context.Paged ? context.Data : null);
                    context.ReportProgress(Name, $"Processing RAM budget: {context.Resources.MemoryBytes / 1073741824.0:G4} GiB; shared page cache {context.Resources.CacheBytes / 1048576:N0} MiB. Storage mode: {context.Resources.Mode}.");
                    var volume = depthFusion.Fuse(project, scene, maps, cloud, resources, cancellation,
                        message => context.ReportProgress(Name, message), context.Work,
                        context.Paged ? new FusionChunkCheckpoints(context, inputs, fileOperations) : null,
                        update => context.ReportOperation(Name, update),
                        preview => previews.Publish(preview));
                    if (project.Settings.FusionIslandMode != IslandTrimMode.Off)
                    {
                        context.ReportProgress(Name, "Removing disconnected fusion regions before meshing…");
                    }
                    var trimming = FusionIslandTrimmer.Trim(volume, project.Settings.FusionIslandMode,
                        project.Settings.FusionIslandMinimumSizePercent, cancellation, context.Work, message => context.ReportProgress(Name, message));
                    volume = trimming.Volume;
                    if (trimming.InputRegions.HasValue)
                    {
                        context.ReportProgress(Name, $"Removed {trimming.RemovedRegions:N0} fusion regions ({trimming.RemovedSamples:N0} volume samples) before meshing.");
                    }
                    fileOperations.AtomicData(Path.Combine(dir, "volume.json"), volume, cancellation);
                    fileOperations.AtomicData(Path.Combine(dir, "volume.preview.json"), new FusedVolume
                    {
                        VoxelSize = volume.VoxelSize,
                        InputSamples = volume.InputSamples,
                        ConsistentSamples = volume.ConsistentSamples,
                        Resolution = volume.Resolution,
                        Samples = FusionPreviewSelection.Sample(volume.Samples)
                    });
                    fileOperations.AtomicJson(Path.Combine(dir, "fusion.report.json"), new
                    {
                        volume.VoxelSize,
                        volume.InputSamples,
                        volume.ConsistentSamples,
                        voxels = volume.Samples.Count,
                        volume.Resolution,
                        islandTrimming = new
                        {
                            mode = project.Settings.FusionIslandMode,
                            minimumSizePercent = project.Settings.FusionIslandMinimumSizePercent,
                            trimming.InputRegions,
                            trimming.RemovedRegions,
                            trimming.RemovedSamples
                        },
                        method = "Precision-weighted projective TSDF; continuous valid depths contribute truncated free-space evidence in front of the measured surface; occluded and unknown space do not vote; at least two nearby observed depths per retained voxel; resolution and integration uncertainty are independent."
                    });
                });
        context.Publish("fusion", fusionPath);
    }
}
