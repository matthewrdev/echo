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

    [ImportingConstructor]
    public SceneSemanticArtifacts(ISemanticModelProvider modelProvider, ISceneSegmenter segmenter, IFileOperations files)
    {
        this.modelProvider = modelProvider;
        this.segmenter = segmenter;
        this.files = files;
    }

    public IReadOnlyDictionary<string, SemanticMap> Build(RunContext context)
    {
        var maps = new Dictionary<string, SemanticMap>();
        var units = new Dictionary<string, string>();
        var classified = 0;
        ReportPurpose(context);
        ISceneSegmentationSession? session = null;
        try
        {
            foreach (var photo in context.Images)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                var reused = true;
                var unit = context.Build("semantic-image", new SemanticImageInputs
                {
                    Id = photo.Id,
                    Hash = photo.Hash,
                    SourceHash = photo.SourceHash,
                    Fingerprint = modelProvider.Fingerprint
                }, directory =>
                {
                    if (session == null)
                    {
                        var model = modelProvider.EnsureAvailable(context.Store.PathFor("models"), context.Cancellation,
                            message => context.ReportProgress("depth", message));
                        session = segmenter.Open(model);
                    }
                    context.ReportProgress("depth", $"Detecting sky, vegetation and rock locally · {photo.Name} · {maps.Count + 1}/{context.Images.Count}");
                    var map = session.Segment(context.Store.PathFor(photo.Source), photo.Id, context.Cancellation);
                    files.AtomicData(Path.Combine(directory, "semantics.json"), map, context.Cancellation);
                    reused = false;
                    classified++;
                }, algorithmVersion: "echo-semantic-upernet2-sky");
                units.Add(photo.Id, unit);
                maps.Add(photo.Id, context.ReadData<SemanticMap>(context.Store.PathFor(unit + "/semantics.json")));
                var action = reused ? "Reused saved sky, vegetation and rock classification" : "Saved new sky, vegetation and rock classification";
                context.ReportProgress("depth", $"{action} · {photo.Name} · {maps.Count}/{context.Images.Count}", maps.Count, context.Images.Count);
            }
        }
        finally
        {
            session?.Dispose();
        }
        var artifact = context.Build("semantics", units, directory => files.AtomicJson(Path.Combine(directory, "units.json"), units),
            () => new ProcessingMetadata { SourceArtifacts = units.Values.ToList() });
        context.Publish("semantics", artifact);
        context.ReportProgress("depth", $"Photo classification ready: {classified} processed locally, {maps.Count - classified} reused from this project's saved results.");
        return maps;
    }

    private void ReportPurpose(RunContext context)
    {
        var settings = context.Run.Snapshot.Settings;
        var reasons = new List<string>();
        if (settings.DepthSuppressSky)
        {
            reasons.Add("Exclude sky from depth");
        }

        if (settings.RecoverCliffDepth)
        {
            reasons.Add("Recover missing cliff detail");
        }

        if (settings.OptimiseVegetation)
        {
            reasons.Add("Optimise vegetation");
        }

        context.ReportProgress("depth", $"Checking saved sky, vegetation and rock classifications for: {string.Join(", ", reasons)}. Only photos without compatible saved results need local model processing.");
    }
}
