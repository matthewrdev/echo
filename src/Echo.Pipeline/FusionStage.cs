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

    [ImportingConstructor]
    public FusionStage(IFileOperations fileOperations, IJsonCodec jsonCodec, ICloudBuilder cloudBuilder, IDepthFusion depthFusion)
    {
        this.fileOperations = fileOperations;
        this.jsonCodec = jsonCodec;
        this.cloudBuilder = cloudBuilder;
        this.depthFusion = depthFusion;
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
        var fusionPath = context.Build("fusion", new
        {
            cloudPath,
            depthUnits,
            alignment,
            project.Settings.FaceLimit,
            fusionVersion = 2,
            project.Settings.FusionDetailMultiplier
        }, dir =>
                {
                    var cloud = cloudBuilder.Read(store.PathFor(cloudPath + "/cloud.ply"), jsonCodec.Read<DenseCloudReport>(store.PathFor(cloudPath + "/cloud.json")));
                    var volume = depthFusion.Fuse(project, scene, maps, cloud, cancellation);
                    fileOperations.AtomicJson(Path.Combine(dir, "volume.json"), volume);
                    fileOperations.AtomicJson(Path.Combine(dir, "volume.preview.json"), new FusedVolume
                    {
                        VoxelSize = volume.VoxelSize,
                        InputSamples = volume.InputSamples,
                        ConsistentSamples = volume.ConsistentSamples,
                        Samples = volume.Samples.Where((_, index) => index % Math.Max(1, volume.Samples.Count / 80000) == 0).ToList()
                    });
                    fileOperations.AtomicJson(Path.Combine(dir, "fusion.report.json"), new
                    {
                        volume.VoxelSize,
                        volume.InputSamples,
                        volume.ConsistentSamples,
                        voxels = volume.Samples.Count,
                        method = "Projective TSDF; at least two observed depths per retained voxel; unobserved space is not filled."
                    });
                });
        context.Publish("fusion", fusionPath);
    }
}
