using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Matching;
using Echo.Optimization;
using Echo.Reconstruction;

namespace Echo.Pipeline;

[Export(typeof(IPipelineStage)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class AlignmentStage : IPipelineStage
{
    private readonly IFileOperations fileOperations;
    private readonly IJsonCodec jsonCodec;
    private readonly IReconstructor reconstructor;
    private readonly IPlyWriter plyWriter;
    private readonly IGuidedFeatureMatcher guidedMatcher;
    private readonly IBundleAdjuster bundleAdjuster;

    [ImportingConstructor]
    public AlignmentStage(IFileOperations fileOperations, IJsonCodec jsonCodec, IReconstructor reconstructor, IPlyWriter plyWriter, IGuidedFeatureMatcher guidedMatcher, IBundleAdjuster bundleAdjuster)
    {
        this.fileOperations = fileOperations;
        this.jsonCodec = jsonCodec;
        this.reconstructor = reconstructor;
        this.plyWriter = plyWriter;
        this.guidedMatcher = guidedMatcher;
        this.bundleAdjuster = bundleAdjuster;
    }

    public string Name => "alignment";
    public int Order => 2;

    public void Execute(RunContext context)
    {
        var project = context.Run.Snapshot;
        var pairUnits = context.ReadArtifact<List<string>>("matching", "units.json");
        var pairs = new List<ImagePair>(pairUnits.Count);
        foreach (var unit in pairUnits)
        {
            pairs.Add(jsonCodec.Read<ImagePair>(context.Store.PathFor(unit + "/pair.json")));
        }
        var initial = context.Build("alignment-initial", new
        {
            algorithmVersion = 4,
            pairUnits,
            cameras = context.Images,
            project.Settings.BundleIterations,
            hints = project.Hints
        }, directory =>
        {
            context.ReportProgress(Name, "Initial alignment: verifying image geometry and refining camera poses.");
            var scene = reconstructor.Align(project, pairs, context.Cancellation, preview => PublishPreview(context, preview));
            fileOperations.AtomicData(Path.Combine(directory, "scene.json"), scene);
        });

        var recoveryUnits = project.Settings.GuidedAlignmentPass ? RecoverPairs(context, pairUnits, pairs) : [];
        var alignment = context.Build(Name, new
        {
            algorithmVersion = 4,
            initial,
            recoveryUnits,
            project.Settings.GuidedAlignmentPass,
            project.Settings.BundleIterations
        }, directory =>
        {
            var scene = jsonCodec.Read<ReconstructionScene>(context.Store.PathFor(initial + "/scene.json"));
            var originalPairs = new List<ImagePair>(pairs);
            var recoveredPairs = ApplyRecoveredPairs(context, pairs, recoveryUnits);
            AlignmentRecoveryAssessment? assessment = null;
            if (recoveredPairs > 0)
            {
                context.ReportProgress(Name, $"Second alignment: jointly refining cameras and points with {recoveredPairs:N0} strengthened image pairs.");
                var validated = AlignmentRecoveryFilter.Filter(project, scene, originalPairs, pairs, context.Cancellation,
                    (completed, total) => context.ReportProgress(Name, $"Second pass: checking image pair {completed:N0} of {total:N0} against existing 3D landmarks.", completed, total));
                // Keep speculative recovery local until its support has been assessed.
                var established = jsonCodec.Read<ReconstructionScene>(context.Store.PathFor(initial + "/scene.json"));
                try
                {
                    bundleAdjuster.Refine(project, scene, validated, context.Cancellation);
                    assessment = AlignmentRecoveryEvaluator.Evaluate(established, scene);
                }
                catch (EchoException error) when (error.ExitCode == 5)
                {
                    assessment = new AlignmentRecoveryAssessment
                    {
                        Reason = $"Second pass could not produce a stable solution: {error.Message} Kept the initial verified alignment."
                    };
                }
                if (!assessment.Accepted)
                {
                    scene = established;
                    scene.Warnings.Add(assessment.Reason);
                }
                context.ReportProgress(Name, assessment.Reason);
                PublishPreview(context, scene);
            }
            fileOperations.AtomicJson(Path.Combine(directory, "recovery.json"), new
            {
                initial,
                recoveryUnits,
                recoveredPairs,
                assessment
            });
            fileOperations.AtomicData(Path.Combine(directory, "scene.json"), scene);
            var positions = new double[scene.Points.Count][];
            for (var index = 0; index < positions.Length; index++)
            {
                positions[index] = scene.Points[index].Position;
            }
            plyWriter.Write(Path.Combine(directory, "points.ply"), positions);
        });
        context.Publish(Name, alignment);
    }

    private List<string> RecoverPairs(RunContext context, List<string> pairUnits, List<ImagePair> pairs)
    {
        var features = context.ReadArtifact<Dictionary<string, string>>("features", "units.json");
        var photos = new Dictionary<string, Photo>(context.Images.Count, StringComparer.Ordinal);
        foreach (var photo in context.Images)
        {
            photos.Add(photo.Id, photo);
        }
        var settings = context.Run.Snapshot.Settings;
        var units = new List<string>(pairs.Count);
        for (var index = 0; index < pairs.Count; index++)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var pair = pairs[index];
            var threshold = Math.Max(photos[pair.A].Width, photos[pair.B].Width) / 1000.0;
            var unit = context.Build("alignment-guided-pair", new
            {
                algorithmVersion = 1,
                initialPair = pairUnits[index],
                left = features[pair.A],
                right = features[pair.B],
                threshold,
                settings.MatchRatioThreshold,
                settings.MatchDistanceThreshold
            }, directory =>
            {
                var left = jsonCodec.Read<FeatureSet>(context.Store.PathFor(features[pair.A] + "/features.json"));
                var right = jsonCodec.Read<FeatureSet>(context.Store.PathFor(features[pair.B] + "/features.json"));
                var recovered = guidedMatcher.Recover(left, right, pair.Matches, settings, threshold, context.Cancellation);
                fileOperations.AtomicJson(Path.Combine(directory, "pair.json"), recovered ?? new ImagePair { A = pair.A, B = pair.B });
            });
            units.Add(unit);
            context.ReportProgress(Name, $"Second pass: searching and validating overlap in image pair {index + 1:N0} of {pairs.Count:N0}.", index + 1, pairs.Count);
        }
        return units;
    }

    private int ApplyRecoveredPairs(RunContext context, List<ImagePair> pairs, List<string> units)
    {
        var count = 0;
        for (var index = 0; index < units.Count; index++)
        {
            var recovered = jsonCodec.Read<ImagePair>(context.Store.PathFor(units[index] + "/pair.json"));
            if (recovered.Matches.Count == 0)
            {
                continue;
            }
            foreach (var match in pairs[index].Matches)
            {
                if (match.Manual)
                {
                    recovered.Matches.Add(match);
                }
            }
            recovered.LineMatches = pairs[index].LineMatches;
            pairs[index] = recovered;
            count++;
        }
        return count;
    }

    private void PublishPreview(RunContext context, ReconstructionScene scene)
    {
        var preview = context.Build("alignment-preview", scene,
            directory => fileOperations.AtomicData(Path.Combine(directory, "scene.json"), scene));
        context.Publish("alignmentPreview", preview);
    }
}
