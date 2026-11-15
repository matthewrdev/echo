using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.SceneSemantics;

namespace Echo.Pipeline;

[Export(typeof(ISceneSemanticArtifacts)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class SceneSemanticArtifacts : ISceneSemanticArtifacts
{
    private readonly ISemanticModelProvider modelProvider;
    private readonly ISceneSegmenter segmenter;
    private readonly IFileOperations files;
    private readonly IJsonCodec json;

    [ImportingConstructor]
    public SceneSemanticArtifacts(ISemanticModelProvider modelProvider, ISceneSegmenter segmenter, IFileOperations files, IJsonCodec json)
    {
        this.modelProvider = modelProvider;
        this.segmenter = segmenter;
        this.files = files;
        this.json = json;
    }

    public IReadOnlyDictionary<string, SemanticMap> Build(RunContext context)
    {
        var maps = new Dictionary<string, SemanticMap>();
        var units = new Dictionary<string, string>();
        ISceneSegmentationSession? session = null;
        try
        {
            foreach (var photo in context.Images)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                var unit = context.Build("semantic-image", new
                {
                    photo.Id,
                    photo.Hash,
                    photo.SourceHash,
                    modelProvider.Fingerprint
                }, directory =>
                {
                    if (session == null)
                    {
                        var model = modelProvider.EnsureAvailable(context.Store.PathFor("models"), context.Cancellation,
                            message => context.ReportProgress("depth", message));
                        session = segmenter.Open(model);
                    }
                    var map = session.Segment(context.Store.PathFor(photo.Source), photo.Id, context.Cancellation);
                    files.AtomicJson(Path.Combine(directory, "semantics.json"), map);
                }, algorithmVersion: "echo-semantic-upernet1");
                units.Add(photo.Id, unit);
                maps.Add(photo.Id, json.Read<SemanticMap>(context.Store.PathFor(unit + "/semantics.json")));
                context.ReportProgress("depth", $"Classified vegetation and rock · {photo.Name} · {maps.Count}/{context.Images.Count}", maps.Count, context.Images.Count);
            }
        }
        finally
        {
            session?.Dispose();
        }
        var artifact = context.Build("semantics", units, directory => files.AtomicJson(Path.Combine(directory, "units.json"), units),
            () => new ProcessingMetadata { SourceArtifacts = units.Values.ToList() });
        context.Publish("semantics", artifact);
        return maps;
    }
}
