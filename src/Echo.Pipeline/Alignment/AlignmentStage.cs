using Echo.Compute;
using Echo.Infrastructure;
using Echo.Matching;
using Echo.Models;
using Echo.Optimization;
using Echo.Pipeline.Exporting;
using Echo.Pipeline.Features;
using Echo.Pipeline.Runs;
using Echo.Reconstruction;

namespace Echo.Pipeline.Alignment;

public sealed class AlignmentStage : IPipelineStage
{
    private readonly IFileOperations fileOperations;
    private readonly IJsonCodec jsonCodec;
    private readonly IReconstructor reconstructor;
    private readonly IPlyWriter plyWriter;
    private readonly IGuidedFeatureMatcher guidedMatcher;
    private readonly IBundleAdjuster bundleAdjuster;
    private readonly IImageWorkPlanner workPlanner;
    private readonly IOrderedWorkExecutor workExecutor;
    private readonly IFeatureSetCacheFactory cacheFactory;
    private readonly IAlignmentPreviewWriter previewWriter;

    public AlignmentStage(IFileOperations fileOperations, IJsonCodec jsonCodec, IReconstructor reconstructor, IPlyWriter plyWriter,
        IGuidedFeatureMatcher guidedMatcher, IBundleAdjuster bundleAdjuster, IImageWorkPlanner workPlanner,
        IOrderedWorkExecutor workExecutor, IFeatureSetCacheFactory cacheFactory, IAlignmentPreviewWriter previewWriter)
    {
        this.fileOperations = fileOperations;
        this.jsonCodec = jsonCodec;
        this.reconstructor = reconstructor;
        this.plyWriter = plyWriter;
        this.guidedMatcher = guidedMatcher;
        this.bundleAdjuster = bundleAdjuster;
        this.workPlanner = workPlanner;
        this.workExecutor = workExecutor;
        this.cacheFactory = cacheFactory;
        this.previewWriter = previewWriter;
    }

    public string Name => "alignment";
    public int Order => 2;

    public void Execute(RunContext context)
    {
        var progress = new AlignmentProgress(context.Run.Snapshot.Settings.GuidedAlignmentPass,
            update => context.ReportOperation(Name, update));
        progress.Begin(AlignmentPhase.LoadMatches, "Loading verified image correspondences…");
        var previewPublisher = context.Headless ? null : new RunPreviewPublisher(context, fileOperations, RunPreviewStage.Alignment);
        void Preview(ReconstructionScene scene)
        {
            if (previewPublisher?.IsDue == true && scene.Points.Count > 0)
            {
                previewPublisher.PublishAlignment(AlignmentPreviewSampler.Sample(scene), previewWriter);
            }
        }
        var project = context.Run.Snapshot;
        var pairUnits = context.ReadArtifact<List<string>>("matching", "units.json");
        var pairs = new List<ImagePair>(pairUnits.Count);
        foreach (var unit in pairUnits)
        {
            pairs.Add(jsonCodec.Read<ImagePair>(context.Store.PathFor(unit + "/pair.json")));
        }

        var initial = context.Build("alignment-initial", new
        {
            algorithmVersion = 6,
            project.Settings.ComputeBackend,
            computeImplementation = context.ComputeImplementation(ComputeOperation.Alignment),
            pairUnits,
            cameras = context.Images,
            project.Settings.BundleIterations,
            hints = project.Hints
        }, directory =>
        {
            progress.Begin(AlignmentPhase.InitialSolve, "Initial alignment: verifying image geometry and refining camera poses.");
            var scene = reconstructor.Align(project, pairs, context.Cancellation, context.Headless ? null : Preview, progress.Report);
            progress.Begin(AlignmentPhase.SaveInitial, "Saving initial cameras and sparse points…");
            fileOperations.AtomicData(Path.Combine(directory, "scene.json"), scene);
        });

        var recoveryUnits = project.Settings.GuidedAlignmentPass ? RecoverPairs(context, pairUnits, pairs, progress) : [];
        var alignment = context.Build(Name, new
        {
            algorithmVersion = 6,
            project.Settings.ComputeBackend,
            computeImplementation = context.ComputeImplementation(ComputeOperation.Alignment),
            initial,
            recoveryUnits,
            project.Settings.GuidedAlignmentPass,
            project.Settings.BundleIterations
        }, directory =>
        {
            if (project.Settings.GuidedAlignmentPass)
            {
                progress.Begin(AlignmentPhase.ValidateRecovery, "Checking recovered image correspondences…");
            }

            var scene = jsonCodec.Read<ReconstructionScene>(context.Store.PathFor(initial + "/scene.json"));
            var originalPairs = new List<ImagePair>(pairs);
            var recoveredPairs = ApplyRecoveredPairs(context, pairs, recoveryUnits);
            AlignmentRecoveryAssessment? assessment = null;
            if (recoveredPairs > 0)
            {
                context.ReportProgress(Name, $"Second alignment: jointly refining cameras and points with {recoveredPairs:N0} strengthened image pairs.");
                var validated = AlignmentRecoveryFilter.Filter(project, scene, originalPairs, pairs, context.Cancellation,
                    (completed, total) => progress.Report(new OperationProgress("Validate recovery",
                        $"Checking image pair {completed:N0} of {total:N0} against existing 3D landmarks.", completed, total, "image pairs")));
                // Keep speculative recovery local until its support has been assessed.
                var established = jsonCodec.Read<ReconstructionScene>(context.Store.PathFor(initial + "/scene.json"));
                try
                {
                    progress.Begin(AlignmentPhase.RefineRecovery, "Refining cameras and points with validated recovered matches…");
                    bundleAdjuster.Refine(project, scene, validated, context.Cancellation, context.Headless ? null : Preview, progress.Report);
                    progress.Begin(AlignmentPhase.AssessRecovery, "Comparing the recovered solution with the initial alignment…");
                    assessment = AlignmentRecoveryEvaluator.Evaluate(established, scene);
                }
                catch (EchoException error) when (error.ExitCode == 5)
                {
                    progress.Begin(AlignmentPhase.AssessRecovery, "Checking recovery stability…");
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
                Preview(scene);
            }

            progress.Begin(AlignmentPhase.SaveAlignment, "Saving the verified alignment…");
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
            progress.Report(new OperationProgress("Save alignment", "Cameras and sparse points saved.", 3, 3, "files"));
        });
        context.Publish(Name, alignment);
    }

    private List<string> RecoverPairs(RunContext context, List<string> pairUnits, List<ImagePair> pairs, AlignmentProgress progress)
    {
        progress.Begin(AlignmentPhase.GuidedMatching, "Searching for additional verified image matches…");
        var settings = context.Run.Snapshot.Settings;
        var features = settings.ExcludeVegetationFromMatching
            ? context.ReadArtifact<Dictionary<string, string>>("matching", "feature-units.json")
            : context.ReadArtifact<Dictionary<string, string>>("features", "units.json");
        var photos = new Dictionary<string, Photo>(context.Images.Count, StringComparer.Ordinal);
        foreach (var photo in context.Images)
        {
            photos.Add(photo.Id, photo);
        }

        var units = new List<string>(pairs.Count);
        var plan = workPlanner.Matching(settings, pairs.Count);
        using var cache = cacheFactory.Create(plan.FeatureCacheBytes);
        workExecutor.Run(pairs.Count, plan.Workers,
            index => BuildRecoveryPair(context, pairs[index], pairUnits[index], photos, features, cache), (index, unit) =>
        {
            units.Add(unit);
            progress.Report(new OperationProgress("Guided matching",
                $"Searching and validating overlap in image pair {index + 1:N0} of {pairs.Count:N0}.", index + 1, pairs.Count, "image pairs"));
        }, context.Cancellation);
        return units;
    }

    private string BuildRecoveryPair(RunContext context, ImagePair pair, string initialPair,
        IReadOnlyDictionary<string, Photo> photos, IReadOnlyDictionary<string, string> features, FeatureSetCache cache)
    {
        var settings = context.Run.Snapshot.Settings;
        var threshold = Math.Max(photos[pair.A].Width, photos[pair.B].Width) / 1000.0;
        return context.Build("alignment-guided-pair", new
        {
            algorithmVersion = 2,
            settings.ComputeBackend,
            computeImplementation = context.ComputeImplementation(ComputeOperation.Alignment),
            initialPair,
            left = features[pair.A],
            right = features[pair.B],
            threshold,
            settings.MatchRatioThreshold,
            settings.MatchDistanceThreshold
        }, directory =>
        {
            var left = cache.Read(context.Store.PathFor(features[pair.A] + "/features.json"), context.Cancellation);
            var right = cache.Read(context.Store.PathFor(features[pair.B] + "/features.json"), context.Cancellation);
            var recovered = guidedMatcher.Recover(left, right, pair.Matches, settings, threshold, context.Cancellation);
            fileOperations.AtomicJson(Path.Combine(directory, "pair.json"), recovered ?? new ImagePair { A = pair.A, B = pair.B });
        });
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

}
