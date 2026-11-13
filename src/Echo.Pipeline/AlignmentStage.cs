using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Reconstruction;

namespace Echo.Pipeline;

[Export(typeof(IPipelineStage)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class AlignmentStage : IPipelineStage
{
    private readonly IFileOperations fileOperations;
    private readonly IJsonCodec jsonCodec;
    private readonly IReconstructor reconstructor;
    private readonly IPlyWriter plyWriter;

    [ImportingConstructor]
    public AlignmentStage(IFileOperations fileOperations, IJsonCodec jsonCodec, IReconstructor reconstructor, IPlyWriter plyWriter)
    {
        this.fileOperations = fileOperations;
        this.jsonCodec = jsonCodec;
        this.reconstructor = reconstructor;
        this.plyWriter = plyWriter;
    }

    public string Name => "alignment";
    public int Order => 2;

    public void Execute(RunContext context)
    {
        var project = context.Run.Snapshot;
        var images = context.Images;
        var store = context.Store;
        var cancellation = context.Cancellation;
        var pairUnits = context.ReadArtifact<List<string>>("matching", "units.json");
        var pairs = pairUnits.Select(p => jsonCodec.Read<ImagePair>(store.PathFor(p + "/pair.json"))).ToList();
        var alignment = context.Build("alignment", new
        {
            pairUnits,
            cameras = images,
            project.Settings.BundleIterations,
            hints = project.Hints.Where(h => h.Enabled && h.Type == "camera")
        }, dir =>
                {
                    var reconstruction = reconstructor.Align(project, pairs, cancellation, scene =>
                    {
                        var preview = context.Build("alignment-preview", scene, d => fileOperations.AtomicJson(Path.Combine(d, "scene.json"), scene));
                        context.Publish("alignmentPreview", preview);
                    });
                    fileOperations.AtomicJson(Path.Combine(dir, "scene.json"), reconstruction);
                    plyWriter.Write(Path.Combine(dir, "points.ply"), reconstruction.Points.Select(p => p.Position).ToArray());
                });
        context.Publish("alignment", alignment);
    }
}
