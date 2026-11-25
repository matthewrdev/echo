using Echo.Compute;
using Echo.Infrastructure;
using Echo.Matching;
using Echo.Models;
using Echo.Optimization;
using Echo.Pipeline.Exporting;
using Echo.Pipeline.Features;
using Echo.Pipeline.Runs;
using Echo.Reconstruction;
using Echo.Storage.Matching;

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
    private readonly IMatchArtifactStore matchArtifacts;
    private readonly ISurveyControl surveyControl;

    public AlignmentStage(IFileOperations fileOperations, IJsonCodec jsonCodec, IReconstructor reconstructor, IPlyWriter plyWriter,
        IGuidedFeatureMatcher guidedMatcher, IBundleAdjuster bundleAdjuster, IImageWorkPlanner workPlanner,
        IOrderedWorkExecutor workExecutor, IFeatureSetCacheFactory cacheFactory, IAlignmentPreviewWriter previewWriter, IMatchArtifactStore matchArtifacts,
        ISurveyControl surveyControl)
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
        this.matchArtifacts = matchArtifacts;
        this.surveyControl = surveyControl;
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
        List<ImagePair>? loadedPairs = null;
        ImagePair LoadPair(int index)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            return loadedPairs?[index] ?? matchArtifacts.Read(context.Store.PathFor(pairUnits[index] + "/pair.json"));
        }

        List<ImagePair> LoadPairs() => loadedPairs ??= Enumerable.Range(0, pairUnits.Count).Select(LoadPair).ToList();

        var initial = context.Build("alignment-initial", new Dictionary<string, object?>
        {
            ["algorithmVersion"] = 9,
            ["computeBackend"] = project.Settings.ComputeBackend,
            ["computeImplementation"] = context.ComputeImplementation(ComputeOperation.Alignment),
            ["pairUnits"] = pairUnits,
            ["cameras"] = context.Images,
            ["bundleIterations"] = project.Settings.BundleIterations,
            ["hints"] = project.Hints
        }, directory =>
        {
            progress.Begin(AlignmentPhase.InitialSolve, "Initial alignment: verifying image geometry and refining camera poses.");
            var scene = reconstructor.Align(project, LoadPairs(), context.Cancellation, context.Headless ? null : Preview, progress.Report);
            progress.Begin(AlignmentPhase.SaveInitial, "Saving initial cameras and sparse points…");
            fileOperations.AtomicData(Path.Combine(directory, "scene.json"), scene);
            WriteSparsePoints(directory, scene);
        }, sources: pairUnits);

        var recoveryUnits = project.Settings.GuidedAlignmentPass ? RecoverPairs(context, pairUnits, loadedPairs, LoadPair, progress) : [];
        var hasSurveyPoints = project.Hints.Any(hint => hint.Enabled && hint.Type is "ground-control" or "survey-check");
        var alignment = context.Build(Name, new Dictionary<string, object?>
        {
            ["algorithmVersion"] = hasSurveyPoints ? 8 : 7,
            ["computeBackend"] = project.Settings.ComputeBackend,
            ["computeImplementation"] = context.ComputeImplementation(ComputeOperation.Alignment),
            ["initial"] = initial,
            ["recoveryUnits"] = recoveryUnits,
            ["guidedAlignmentPass"] = project.Settings.GuidedAlignmentPass,
            ["bundleIterations"] = project.Settings.BundleIterations
        }, directory =>
        {
            var initialScene = jsonCodec.Read<ReconstructionScene>(context.Store.PathFor(initial + "/scene.json"));
            ReconstructionScene? repaired = null;
            if (initialScene.Origin != null && initialScene.Bundle != null)
            {
                var finalPhase = project.Settings.GuidedAlignmentPass ? AlignmentPhase.ValidateRecovery : AlignmentPhase.SaveAlignment;
                progress.Begin(finalPhase, "Checking camera poses against verified photo overlaps…");
                repaired = reconstructor.TryReconcileOverlaps(project, initialScene, LoadPairs(), context.Cancellation, progress.Report);
                if (repaired != null)
                {
                    context.ReportProgress(Name, "Reconciled inconsistent camera overlaps and rebuilt sparse points.");
                    Preview(repaired);
                }
            }

            if (project.Settings.GuidedAlignmentPass)
            {
                progress.Begin(AlignmentPhase.ValidateRecovery, "Checking recovered image correspondences…");
            }

            var pairs = recoveryUnits.Count == 0 ? [] : LoadPairs();
            var originalPairs = new List<ImagePair>(pairs);
            var recoveredPairs = ApplyRecoveredPairs(context, pairs, recoveryUnits);
            AlignmentRecoveryAssessment? assessment = null;
            IReadOnlyList<ImagePair>? acceptedRecovery = null;
            var scene = repaired;
            if (recoveredPairs > 0)
            {
                var established = repaired ?? initialScene;
                context.ReportProgress(Name, $"Second alignment: jointly refining cameras and points with {recoveredPairs:N0} strengthened image pairs.");
                var validated = AlignmentRecoveryFilter.Filter(project, established, originalPairs, pairs, context.Cancellation,
                    (completed, total) => progress.Report(new OperationProgress("Validate recovery",
                        $"Checking image pair {completed:N0} of {total:N0} against existing 3D landmarks.", completed, total, "image pairs")));
                // Keep speculative recovery local until its support has been assessed.
                scene = CopyForBundle(established);
                try
                {
                    progress.Begin(AlignmentPhase.RefineRecovery, "Refining cameras and points with validated recovered matches…");
                    bundleAdjuster.Refine(project, scene, validated, context.Cancellation, context.Headless ? null : Preview, progress.Report);
                    progress.Begin(AlignmentPhase.AssessRecovery, "Comparing the recovered solution with the initial alignment…");
                    assessment = AlignmentRecoveryEvaluator.Evaluate(established, scene);
                    if (assessment.Accepted)
                    {
                        acceptedRecovery = validated;
                    }
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

            if (hasSurveyPoints)
            {
                scene ??= initialScene;
                var survey = surveyControl.Apply(project, scene, context.Cancellation);
                var controls = survey.Points.Where(point => point.UsedAsControl).Select(point => point.Id).ToHashSet(StringComparer.Ordinal);
                if (controls.Count >= 3)
                {
                    context.ReportProgress(Name, "Refining the final alignment against surveyed ground control.");
                    bundleAdjuster.Refine(project, scene, acceptedRecovery ?? LoadPairs(), context.Cancellation, progress: progress.Report,
                        groundControlIds: controls);
                    survey = surveyControl.Measure(project, scene, controls, context.Cancellation);
                }

                fileOperations.AtomicJson(Path.Combine(directory, "survey-control.json"), survey);
            }

            progress.Begin(AlignmentPhase.SaveAlignment, "Saving the verified alignment…");
            fileOperations.AtomicJson(Path.Combine(directory, "recovery.json"), new
            {
                initial,
                recoveryUnits,
                recoveredPairs,
                assessment
            });
            if (scene == null)
            {
                ShareInitialScene(context, initial, directory);
            }
            else
            {
                fileOperations.AtomicData(Path.Combine(directory, "scene.json"), scene);
                WriteSparsePoints(directory, scene);
            }

            progress.Report(new OperationProgress("Save alignment", "Cameras and sparse points saved.", 3, 3, "files"));
        }, sources: [initial, .. recoveryUnits]);
        context.Publish(Name, alignment);
    }

    private ReconstructionScene CopyForBundle(ReconstructionScene scene) => new()
    {
        Origin = scene.Origin,
        Frame = scene.Frame,
        Unplaced = [.. scene.Unplaced],
        Warnings = [.. scene.Warnings],
        Cameras = scene.Cameras.Select(camera => new CameraPose
        {
            ImageId = camera.ImageId,
            Centre = [.. camera.Centre],
            Rotation = [.. camera.Rotation],
            Source = camera.Source,
            FocalPixels = camera.FocalPixels
        }).ToList()
    };

    private void ShareInitialScene(RunContext context, string initial, string directory)
    {
        var source = context.Store.PathFor(initial);
        var manifest = jsonCodec.Read<Artifact>(Path.Combine(source, "manifest.json"));
        foreach (var entry in manifest.Files)
        {
            var name = entry.Key;
            var isPayload = name.StartsWith("scene-", StringComparison.Ordinal) && name.EndsWith(".bin", StringComparison.Ordinal)
                && name == Path.GetFileName(name);
            if (name is "scene.json" or "points.ply" || isPayload)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                fileOperations.CopyVerified(Path.Combine(source, name), Path.Combine(directory, name), entry.Value);
            }
        }

        if (!manifest.Files.ContainsKey("points.ply"))
        {
            var scene = context.ReadData<ReconstructionScene>(Path.Combine(source, "scene.json"));
            WriteSparsePoints(directory, scene);
        }
    }

    private void WriteSparsePoints(string directory, ReconstructionScene scene)
    {
        var positions = new double[scene.Points.Count][];
        for (var index = 0; index < positions.Length; index++)
        {
            positions[index] = scene.Points[index].Position;
        }

        plyWriter.Write(Path.Combine(directory, "points.ply"), positions);
    }

    private List<string> RecoverPairs(RunContext context, List<string> pairUnits, List<ImagePair>? pairs,
        Func<int, ImagePair> loadPair, AlignmentProgress progress)
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

        var units = new List<string>(pairUnits.Count);
        var plan = workPlanner.Matching(settings, pairUnits.Count);
        using var cache = cacheFactory.Create(plan.FeatureCacheBytes);
        workExecutor.Run(pairUnits.Count, plan.Workers, index =>
        {
            var initialPair = pairUnits[index];
            var saved = pairs?[index];
            return BuildRecoveryPair(context, initialPair, saved, () => loadPair(index), photos, features, cache);
        }, (index, unit) =>
        {
            units.Add(unit);
            progress.Report(new OperationProgress("Guided matching",
                $"Searching and validating overlap in image pair {index + 1:N0} of {pairUnits.Count:N0}.", index + 1, pairUnits.Count, "image pairs"));
        }, context.Cancellation);
        return units;
    }

    private string BuildRecoveryPair(RunContext context, string initialPair, ImagePair? saved, Func<ImagePair> loadPair,
        IReadOnlyDictionary<string, Photo> photos, IReadOnlyDictionary<string, string> features, FeatureSetCache cache)
    {
        var settings = context.Run.Snapshot.Settings;
        var header = saved == null ? matchArtifacts.Header(context.Store.PathFor(initialPair + "/pair.json")) : null;
        var leftId = saved?.A ?? header!.A;
        var rightId = saved?.B ?? header!.B;
        var threshold = Math.Max(photos[leftId].Width, photos[rightId].Width) / 1000.0;
        return context.Build("alignment-guided-pair", new Dictionary<string, object?>
        {
            ["algorithmVersion"] = 2,
            ["computeBackend"] = settings.ComputeBackend,
            ["computeImplementation"] = context.ComputeImplementation(ComputeOperation.Alignment),
            ["initialPair"] = initialPair,
            ["left"] = features[leftId],
            ["right"] = features[rightId],
            ["threshold"] = threshold,
            ["matchRatioThreshold"] = settings.MatchRatioThreshold,
            ["matchDistanceThreshold"] = settings.MatchDistanceThreshold
        }, directory =>
        {
            var left = cache.Read(context.Store.PathFor(features[leftId] + "/features.json"), context.Cancellation);
            var right = cache.Read(context.Store.PathFor(features[rightId] + "/features.json"), context.Cancellation);
            var pair = saved ?? loadPair();
            var recovered = guidedMatcher.Recover(left, right, pair.Matches, settings, threshold, context.Cancellation);
            fileOperations.AtomicJson(Path.Combine(directory, "pair.json"), recovered ?? new ImagePair { A = pair.A, B = pair.B });
        }, sources: [initialPair, features[leftId], features[rightId]]);
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
