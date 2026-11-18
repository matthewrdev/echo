using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Depth;
using Echo.Infrastructure;

namespace Echo.Pipeline;

[Export(typeof(ICliffRecoveryArtifacts)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class CliffRecoveryArtifacts : ICliffRecoveryArtifacts
{
    private readonly ICliffDepthRecovery recovery;
    private readonly IJsonCodec json;
    private readonly IFileOperations files;

    [ImportingConstructor]
    public CliffRecoveryArtifacts(ICliffDepthRecovery recovery, IJsonCodec json, IFileOperations files)
    {
        this.recovery = recovery;
        this.json = json;
        this.files = files;
    }

    public string[] Build(RunContext context, ReconstructionScene scene, string[] originalUnits, IReadOnlyDictionary<string, SemanticMap> semantics, SubjectDistancePlan? subjectDistance = null)
    {
        subjectDistance ??= SubjectDistancePlanner.Estimate(context.Run.Snapshot, scene, context.Cancellation);
        // All confirmations use the original maps. New estimates cannot provide
        // circular support for one another, regardless of image processing order.
        var maps = originalUnits.Select(unit => context.ReadData<DepthMap>(context.Store.PathFor(unit + "/depth.json"))).ToArray();
        var units = new string[maps.Length];
        var photos = context.Images.ToDictionary(photo => photo.Id);
        for (var index = 0; index < maps.Length; index++)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var map = maps[index];
            var photo = photos[map.ImageId];
            units[index] = context.Build("cliff-depth-image", new
            {
                originalUnits,
                map.ImageId,
                alignment = context.Artifact("alignment"),
                semantics = context.Artifact("semantics"),
                context.Run.Snapshot.Settings.DepthNeighbours,
                context.Run.Snapshot.Settings.DepthSuppressSky,
                subjectDistance
            }, directory =>
            {
                var result = recovery.Recover(context.Store, context.Run.Snapshot, scene, map, maps, semantics[map.ImageId], context.Cancellation,
                    message => context.ReportProgress("depth", $"{photo.Name} · {message}", index, maps.Length));
                var distanceRemoved = SubjectDistanceFilter.Apply(result.Map, subjectDistance, context.Cancellation);
                files.AtomicData(Path.Combine(directory, "depth.json"), result.Map, context.Cancellation);
                files.AtomicJson(Path.Combine(directory, "recovery.report.json"), new
                {
                    map.ImageId,
                    result.SearchedSamples,
                    result.RecoveredSamples,
                    result.SourceImageWidth,
                    result.Neighbours,
                    subjectDistanceRemovedSamples = distanceRemoved,
                    preservedSamples = Enumerable.Range(0, map.SampleCount).Count(index => float.IsFinite(map.DepthAt(index)) && map.DepthAt(index) > 0),
                    method = "Bounded missing-rock stereo search; two original depth confirmations and two photographic confirmations; no generative geometry"
                });
            }, () => new ProcessingMetadata { SourceArtifacts = [.. originalUnits, context.Artifact("semantics"), context.Artifact("alignment")] },
                algorithmVersion: "echo-cliff-recovery4-overlap-sky-subject1");
            context.ReportProgress("depth", $"Cliff recovery checked · {photo.Name} · {index + 1}/{maps.Length}", index + 1, maps.Length);
        }
        return units;
    }
}
