using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Meshing;
using Echo.Simplification;

namespace Echo.Pipeline;

[Export(typeof(IPipelineStage)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class MeshStage : IPipelineStage
{
    private readonly IFileOperations fileOperations;
    private readonly IJsonCodec jsonCodec;
    private readonly IMeshBuilder meshBuilder;
    private readonly IEdgeCollapse edgeCollapse;
    private readonly IPlyWriter plyWriter;

    [ImportingConstructor]
    public MeshStage(IFileOperations fileOperations,
        IJsonCodec jsonCodec,
        IMeshBuilder meshBuilder,
        IEdgeCollapse edgeCollapse,
        IPlyWriter plyWriter)
    {
        this.fileOperations = fileOperations;
        this.jsonCodec = jsonCodec;
        this.meshBuilder = meshBuilder;
        this.edgeCollapse = edgeCollapse;
        this.plyWriter = plyWriter;
    }

    public string Name => "mesh";
    public int Order => 6;

    public void Execute(RunContext context)
    {
        var project = context.Run.Snapshot;
        var store = context.Store;
        var cancellation = context.Cancellation;
        var alignment = context.Artifact("alignment");
        var scene = context.Scene();
        var depthUnits = context.DepthUnits();
        var maps = context.DepthMaps();
        var fusionPath = context.Artifact("fusion");
        var meshPath = context.Build("mesh", new
        {
            fusionPath,
            depthUnits,
            alignment,
            project.Settings.FaceLimit
        }, dir =>
                {
                    var volume = jsonCodec.Read<FusedVolume>(store.PathFor(fusionPath + "/volume.json"));
                    var extracted = meshBuilder.Extract(volume, cancellation);
                    var reduced = edgeCollapse.Reduce(extracted, project.Settings.FaceLimit, cancellation);
                    var mesh = meshBuilder.AssignCameras(project, scene, maps, reduced, cancellation);
                    fileOperations.AtomicJson(Path.Combine(dir, "mesh.json"), mesh);
                    plyWriter.Write(Path.Combine(dir, "mesh.ply"), mesh.Vertices.ToArray(), mesh.Faces.ToArray());
                    fileOperations.AtomicJson(Path.Combine(dir, "mesh.report.json"), new
                    {
                        extractedFaces = extracted.Faces.Count,
                        reducedFaces = reduced.Faces.Count,
                        texturedFaces = mesh.Faces.Count,
                        requestedFaceLimit = project.Settings.FaceLimit,
                        volume.VoxelSize,
                        note = "Target is a ceiling, not fabricated detail. Edge collapse preserves connected patches; unsupported or untexturable regions remain absent."
                    });
                });
        context.Publish("mesh", meshPath);
    }
}
