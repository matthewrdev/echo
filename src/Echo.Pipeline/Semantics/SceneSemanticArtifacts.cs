using Echo.Infrastructure;
using Echo.Models;
using Echo.Pipeline.Runs;
using Echo.SceneSemantics;

namespace Echo.Pipeline.Semantics;

public sealed class SceneSemanticArtifacts : ISceneSemanticArtifacts
{
    private readonly ISemanticModelProvider modelProvider;
    private readonly ISceneSegmenter segmenter;
    private readonly IFileOperations files;
    private readonly IOrderedWorkExecutor workExecutor;

    public SceneSemanticArtifacts(ISemanticModelProvider modelProvider, ISceneSegmenter segmenter, IFileOperations files, IOrderedWorkExecutor workExecutor)
    {
        this.modelProvider = modelProvider;
        this.segmenter = segmenter;
        this.files = files;
        this.workExecutor = workExecutor;
    }

    public IReadOnlyDictionary<string, SemanticMap> Build(RunContext context, string progressStage = "depth")
    {
        var maps = new Dictionary<string, SemanticMap>();
        var units = new Dictionary<string, string>();
        var classified = 0;
        var backend = context.Run.Snapshot.Settings.ComputeBackend;
        var implementation = segmenter.GetImplementation(backend);
        ReportPurpose(context, progressStage);
        ISceneSegmentationSession? session = null;
        var sessionGate = new object();
        var memoryWorkers = Math.Max(1, context.WorkingMemoryBytes / (512L * 1024 * 1024));
        var workers = (int)Math.Clamp(Math.Min(memoryWorkers, Math.Max(1, (Environment.ProcessorCount - 2) / 4)), 1, 2);
        try
        {
            workExecutor.Run(context.Images.Count, workers, index =>
            {
                var photo = context.Images[index];
                context.Cancellation.ThrowIfCancellationRequested();
                var reused = true;
                SemanticMap? detected = null;
                var unit = context.Build("semantic-image", new Dictionary<string, object?>
                {
                    ["id"] = photo.Id,
                    ["hash"] = photo.Hash,
                    ["sourceHash"] = photo.SourceHash,
                    ["fingerprint"] = modelProvider.Fingerprint,
                    ["implementation"] = implementation
                }, directory =>
                {
                    lock (sessionGate)
                    {
                        if (session == null)
                        {
                            var model = modelProvider.EnsureAvailable(context.Store.PathFor("models"), context.Cancellation,
                                message => context.ReportProgress(progressStage, message));
                            session = segmenter.Open(model, backend);
                        }
                    }

                    context.ReportProgress(progressStage, $"Detecting sky, vegetation and rock locally · {photo.Name}");
                    detected = session.Segment(context.Store.PathFor(photo.Source), photo.Id, context.Cancellation);
                    files.AtomicData(Path.Combine(directory, "semantics.json"), detected, context.Cancellation);
                    reused = false;
                }, algorithmVersion: "echo-semantic-upernet2-sky");
                return new SemanticArtifactCheckpoint
                {
                    Unit = unit,
                    Map = detected ?? context.ReadData<SemanticMap>(context.Store.PathFor(unit + "/semantics.json")),
                    Reused = reused
                };
            }, (index, checkpoint) =>
            {
                var photo = context.Images[index];
                units.Add(photo.Id, checkpoint.Unit);
                maps.Add(photo.Id, checkpoint.Map);
                classified += checkpoint.Reused ? 0 : 1;
                var action = checkpoint.Reused ? "Reused saved sky, vegetation and rock classification" : "Saved new sky, vegetation and rock classification";
                context.ReportProgress(progressStage, $"{action} · {photo.Name} · {maps.Count}/{context.Images.Count}",
                    maps.Count, context.Images.Count, unit: "images");
            }, context.Cancellation);
        }
        finally
        {
            session?.Dispose();
        }

        var artifact = context.Build("semantics", units, directory => files.AtomicJson(Path.Combine(directory, "units.json"), units),
            () => new ProcessingMetadata { SourceArtifacts = units.Values.ToList() });
        context.Publish("semantics", artifact);
        context.ReportProgress(progressStage, $"Photo classification ready: {classified} processed locally, {maps.Count - classified} reused from this project's saved results.");
        return maps;
    }

    private void ReportPurpose(RunContext context, string progressStage)
    {
        if (progressStage == "matching")
        {
            context.ReportProgress(progressStage, "Checking saved photo classifications to exclude vegetation from matching.");
            return;
        }

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
