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
        var maps = context.DepthMaps();
        var cloudPath = context.Artifact("cloud");
        var resources = resourcePlanner.Plan(project.Settings);
        var fusionPath = context.Build("fusion", new
        {
            cloudPath,
            depthUnits,
            alignment,
            resources.MaximumGridSamples,
            fusionVersion = 4,
            project.Settings.CloudDepthTolerance,
            project.Settings.FusionDetailMultiplier,
            project.Settings.FusionIslandMode,
            project.Settings.FusionIslandMinimumSizePercent
        }, dir =>
                {
                    var cloud = cloudBuilder.Read(store.PathFor(cloudPath + "/cloud.ply"), jsonCodec.Read<DenseCloudReport>(store.PathFor(cloudPath + "/cloud.json")));
                    var volume = depthFusion.Fuse(project, scene, maps, cloud, resources, cancellation);
                    if (project.Settings.FusionIslandMode != IslandTrimMode.Off)
                    {
                        context.ReportProgress(Name, "Removing disconnected fusion regions before meshing…");
                    }
                    var trimming = FusionIslandTrimmer.Trim(volume, project.Settings.FusionIslandMode,
                        project.Settings.FusionIslandMinimumSizePercent, cancellation);
                    volume = trimming.Volume;
                    if (trimming.InputRegions.HasValue)
                    {
                        context.ReportProgress(Name, $"Removed {trimming.RemovedRegions:N0} fusion regions ({trimming.RemovedSamples:N0} volume samples) before meshing.");
                    }
                    fileOperations.AtomicJson(Path.Combine(dir, "volume.json"), volume);
                    fileOperations.AtomicJson(Path.Combine(dir, "volume.preview.json"), new FusedVolume
                    {
                        VoxelSize = volume.VoxelSize,
                        InputSamples = volume.InputSamples,
                        ConsistentSamples = volume.ConsistentSamples,
                        Resolution = volume.Resolution,
                        Samples = volume.Samples.Where((_, index) => index % Math.Max(1, volume.Samples.Count / 80000) == 0).ToList()
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
                        method = "Projective TSDF; resolution follows verified source-camera footprints and a separate memory budget; the integration band respects input depth uncertainty independently of voxel spacing; at least two observed depths per retained voxel; unobserved space is not filled."
                    });
                });
        context.Publish("fusion", fusionPath);
    }
}
