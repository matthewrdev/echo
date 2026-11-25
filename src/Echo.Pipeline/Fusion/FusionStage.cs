using Echo.Storage.Artifacts;
using Echo.Compute;
using Echo.DenseCloud;
using Echo.Fusion;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Pipeline.Runs;

namespace Echo.Pipeline.Fusion;

public sealed class FusionStage : IPipelineStage
{
    private readonly IFileOperations fileOperations;
    private readonly IJsonCodec jsonCodec;
    private readonly ICloudBuilder cloudBuilder;
    private readonly IDepthFusion depthFusion;
    private readonly IFusionResourcePlanner resourcePlanner;

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
        var resources = resourcePlanner.Plan(project.Settings, context.WorkingMemoryBytes);
        var inputs = new Dictionary<string, object?>
        {
            ["cloudPath"] = cloudPath,
            ["depthUnits"] = depthUnits,
            ["alignment"] = alignment,
            ["maximumGridSamples"] = context.Paged && project.Settings.FusionVoxelSize > 0 ? 0 : resources.MaximumGridSamples,
            ["fusionVersion"] = project.Settings.ExclusionRegions.Any(region => region.Shape == ExclusionShape.ProjectedHull) ? 16 : 15,
            ["exclusionRegions"] = project.Settings.ExclusionRegions,
            ["computeBackend"] = project.Settings.ComputeBackend,
            ["computeImplementation"] = context.ComputeImplementation(ComputeOperation.Fusion),
            ["cloudDepthTolerance"] = project.Settings.CloudDepthTolerance,
            ["fusionDetailMultiplier"] = project.Settings.FusionDetailMultiplier,
            ["fusionVoxelSize"] = project.Settings.FusionVoxelSize,
            ["fusionAllowCoarserResolution"] = project.Settings.FusionAllowCoarserResolution,
            ["fusionIslandMode"] = project.Settings.FusionIslandMode,
            ["fusionIslandMinimumSizePercent"] = project.Settings.FusionIslandMinimumSizePercent
        };
        var gridInputs = new Dictionary<string, object?>
        {
            ["cloudPath"] = cloudPath,
            ["depthUnits"] = depthUnits,
            ["alignment"] = alignment,
            ["maximumGridSamples"] = inputs["maximumGridSamples"],
            ["fusionVersion"] = inputs["fusionVersion"],
            ["exclusionRegions"] = project.Settings.ExclusionRegions,
            ["cloudDepthTolerance"] = project.Settings.CloudDepthTolerance,
            ["fusionDetailMultiplier"] = project.Settings.FusionDetailMultiplier,
            ["fusionVoxelSize"] = project.Settings.FusionVoxelSize,
            ["fusionAllowCoarserResolution"] = project.Settings.FusionAllowCoarserResolution
        };
        var cropGridInputs = GpsCropFingerprint.Apply(gridInputs, project.Settings.GpsPolygons);
        var integrationInputs = new Dictionary<string, object?>
        {
            ["gridInputs"] = cropGridInputs,
            ["computeBackend"] = project.Settings.ComputeBackend,
            ["computeImplementation"] = inputs["computeImplementation"],
            // Auto backend selection and checkpoint boundaries depend on budgets.
            ["memoryBudgetBytes"] = resources.MemoryBudgetBytes,
            ["computeBudget"] = context.WorkingMemoryBytes / 4
        };
        string[] sources = [cloudPath, .. depthUnits, alignment];
        var fusionPath = context.Build("fusion", GpsCropFingerprint.Apply(inputs, project.Settings.GpsPolygons), dir =>
                {
                    var previews = context.Headless ? null : new RunPreviewPublisher(context, fileOperations, RunPreviewStage.Fusion);
                    var maps = context.DepthMaps();
                    var cloud = cloudBuilder.Read(store.PathFor(cloudPath + "/cloud.ply"), new DenseCloudReportCodec().Read(store.PathFor(cloudPath + "/cloud.bin"), includePreview: false), context.Paged ? context.Data : null);
                    context.ReportProgress(Name, $"Processing RAM budget: {context.Resources.MemoryBytes / 1073741824.0:G4} GiB; shared page cache {context.Resources.CacheBytes / 1048576:N0} MiB. Storage mode: {context.Resources.Mode}.");
                    var volume = depthFusion.Fuse(project, scene, maps, cloud, resources, cancellation,
                        message => context.ReportProgress(Name, message), context.Work,
                        context.Paged ? new FusionChunkCheckpoints(context, integrationInputs, fileOperations, sources) : null,
                        update => context.ReportOperation(Name, update),
                        previews == null ? null : preview =>
                        {
                            if (previews.IsDue)
                            {
                                previews.PublishFusion(preview);
                            }
                        },
                        context.Paged ? new FusionGridCheckpoints(context, cropGridInputs, fileOperations, jsonCodec, sources) : null);
                    if (project.Settings.FusionIslandMode != IslandTrimMode.Off)
                    {
                        context.ReportOperation(Name, FusionProgressPlan.Create(FusionPhase.Cleanup, true, "Removing disconnected fusion regions before meshing…"));
                    }

                    var trimming = FusionIslandTrimmer.Trim(volume, project.Settings.FusionIslandMode,
                        project.Settings.FusionIslandMinimumSizePercent, cancellation, context.Work, message => context.ReportProgress(Name, message));
                    volume = trimming.Volume;
                    if (trimming.InputRegions.HasValue)
                    {
                        context.ReportProgress(Name, $"Removed {trimming.RemovedRegions:N0} fusion regions ({trimming.RemovedSamples:N0} volume samples) before meshing.");
                    }

                    context.ReportOperation(Name, FusionProgressPlan.Create(FusionPhase.Save,
                        project.Settings.FusionIslandMode != IslandTrimMode.Off, "Saving the integrated volume…", 0, 1, "volumes"));
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
                    context.ReportOperation(Name, FusionProgressPlan.Create(FusionPhase.Save,
                        project.Settings.FusionIslandMode != IslandTrimMode.Off, "Integrated volume saved.", 1, 1, "volumes"));
                }, sources: sources);
        context.Publish("fusion", fusionPath);
    }
}
