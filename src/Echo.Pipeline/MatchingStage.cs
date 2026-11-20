using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Compute;
using Echo.Infrastructure;
using Echo.Matching;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Pipeline;

[Export(typeof(IPipelineStage)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class MatchingStage : IPipelineStage
{
    private readonly IFileOperations fileOperations;
    private readonly IFeatureMatcher featureMatcher;
    private readonly IImagePairSelector pairSelector;
    private readonly IImageWorkPlanner workPlanner;
    private readonly IOrderedWorkExecutor workExecutor;
    private readonly IFeatureSetCacheFactory cacheFactory;
    private readonly IComputeService? compute;
    private readonly IMatchingIndexStore indexes;
    private readonly ISceneSemanticArtifacts semanticArtifacts;

    [ImportingConstructor]
    public MatchingStage(IImagePairSelector pairSelector, IFileOperations fileOperations, IFeatureMatcher featureMatcher,
        IImageWorkPlanner workPlanner, IOrderedWorkExecutor workExecutor, IFeatureSetCacheFactory cacheFactory,
        IMatchingIndexStore indexes, ISceneSemanticArtifacts semanticArtifacts, [Import(AllowDefault = true)] IComputeService? compute = null)
    {
        this.fileOperations = fileOperations;
        this.featureMatcher = featureMatcher;
        this.pairSelector = pairSelector;
        this.workPlanner = workPlanner;
        this.workExecutor = workExecutor;
        this.cacheFactory = cacheFactory;
        this.compute = compute;
        this.indexes = indexes;
        this.semanticArtifacts = semanticArtifacts;
    }

    public string Name => "matching";
    public int Order => 1;

    public void Execute(RunContext context)
    {
        var project = context.Run.Snapshot;
        var images = context.Images;
        var cancellation = context.Cancellation;
        var featureUnits = context.ReadArtifact<Dictionary<string, string>>("features", "units.json");
        var wanted = pairSelector.Select(project, images, featureUnits);
        var maskVegetation = project.Settings.ExcludeVegetationFromMatching;
        if (maskVegetation)
        {
            featureUnits = BuildFilteredFeatures(context, featureUnits);
            context.ReportProgress(Name, "Matching vegetation-filtered keypoints", 0, wanted.Count);
        }

        var photos = new Dictionary<string, Photo>(images.Count, StringComparer.Ordinal);
        foreach (var photo in images)
        {
            photos.Add(photo.Id, photo);
        }
        context.ReportProgress("matching", $"Selected {wanted.Count:N0} image pairs using viewing overlap, spatial neighbours, capture sequence and hints.");

        var pairUnits = new List<string>();
        var preview = $"runs/{context.Run.Id}/previews/matching";
        using var journal = indexes.Open(context.Store.PathFor(preview + "/pairs.ndjson"));
        if (journal.Count > wanted.Count)
        {
            throw new InvalidDataException("Matching journal contains pairs outside these run inputs.");
        }
        if (context.Run.Artifacts.GetValueOrDefault("matchingPreview") != preview)
        {
            context.Publish("matchingPreview", preview);
        }
        var plan = workPlanner.Matching(project.Settings, wanted.Count);
        using var cache = cacheFactory.Create(plan.FeatureCacheBytes);
        workExecutor.Run(wanted.Count, plan.Workers, index => BuildPair(context, wanted[index], photos, featureUnits, cache), (index, unit) =>
        {
            var pair = wanted[index];
            pairUnits.Add(unit);
            journal.Append(indexes.Describe(context.Store.Root, unit, index));

            context.Report("pairCheckpoint", new
            {
                stage = "matching",
                pair.A,
                pair.B,
                completed = pairUnits.Count,
                total = wanted.Count
            });
        }, cancellation);

        object matchingInputs = maskVegetation ? new
        {
            selectionVersion = 2,
            pairUnits,
            featureUnits
        } : new
        {
            selectionVersion = 2,
            pairUnits
        };
        context.Publish("matching", context.Build("matching", matchingInputs, directory =>
        {
            fileOperations.AtomicJson(Path.Combine(directory, "units.json"), pairUnits);
            if (maskVegetation)
            {
                fileOperations.AtomicJson(Path.Combine(directory, "feature-units.json"), featureUnits);
            }

            journal.CopyTo(Path.Combine(directory, "pairs.ndjson"));
            fileOperations.AtomicJson(Path.Combine(directory, "selection.json"), new
            {
                selectionVersion = 2,
                project.Settings.FrustumPairNeighbours,
                project.Settings.PairSearchDistance,
                pairs = wanted
            });
        }));
    }

    private Dictionary<string, string> BuildFilteredFeatures(RunContext context, IReadOnlyDictionary<string, string> originalUnits)
    {
        var maps = semanticArtifacts.Build(context, Name);
        var masks = context.ReadArtifact<Dictionary<string, string>>("semantics", "units.json");
        var units = new Dictionary<string, string>(context.Images.Count, StringComparer.Ordinal);
        foreach (var photo in context.Images)
        {
            var original = originalUnits[photo.Id];
            var filtered = context.Build("matching-feature-image", new
            {
                original,
                photo.Width,
                photo.Height,
                mask = masks[photo.Id]
            }, directory =>
            {
                var source = context.ReadData<FeatureSet>(context.Store.PathFor(original + "/features.json"));
                var result = VegetationFeatureFilter.Filter(photo, source, maps[photo.Id]);
                fileOperations.AtomicJson(Path.Combine(directory, "features.json"), result);
                context.ReportProgress(Name,
                    $"{photo.Name} · Excluded {source.Features.Count - result.Features.Count:N0} vegetation keypoints");
            }, algorithmVersion: "echo-matching-vegetation1");
            units.Add(photo.Id, filtered);
        }

        return units;
    }

    private string BuildPair(RunContext context, PairIdentity pair, IReadOnlyDictionary<string, Photo> photos, IReadOnlyDictionary<string, string> featureUnits, FeatureSetCache cache)
    {
        var project = context.Run.Snapshot;
        var cancellation = context.Cancellation;
        var hints = project.Hints.Where(hint => AppliesToPair(hint, pair)).ToArray();
        return context.Build("match-pair", new
        {
            a = featureUnits[pair.A],
            b = featureUnits[pair.B],
            hints,
            matcherVersion = 6,
            project.Settings.ComputeBackend,
            computeImplementation = context.ComputeImplementation(ComputeOperation.Matching),
            project.Settings.MatchRatioThreshold,
            project.Settings.MatchDistanceThreshold
        }, directory =>
        {
            context.Trace.Operation(new OperationProgress("Load feature descriptors", "Reading pair inputs"));
            var a = cache.Read(context.Store.PathFor(featureUnits[pair.A] + "/features.json"), cancellation);
            var b = cache.Read(context.Store.PathFor(featureUnits[pair.B] + "/features.json"), cancellation);
            context.Trace.Operation(new OperationProgress("Descriptor matching", "Matching image pair"));
            var matches = featureMatcher.Match(a, b, project.Settings, cancellation);
            foreach (var hint in hints)
            {
                matches.Matches.Add(new Match
                {
                    A = hint.ImageId == pair.A ? hint.Pixel! : hint.OtherPixel!,
                    B = hint.ImageId == pair.A ? hint.OtherPixel! : hint.Pixel!,
                    Manual = true
                });
            }

            context.Trace.Operation(new OperationProgress("Verify image geometry", "Rejecting inconsistent matches"));
            ImagePairVerifier.Verify(photos[pair.A], photos[pair.B], matches, cancellation, compute, project.Settings.ComputeBackend);
            context.Trace.Operation(new OperationProgress("Save matched pair", "Saving verified correspondences", matches.Matches.Count, matches.Matches.Count, "matches"));
            fileOperations.AtomicJson(Path.Combine(directory, "pair.json"), matches);
        });
    }

    private bool AppliesToPair(Hint hint, PairIdentity pair)
    {
        var sameOrder = hint.ImageId == pair.A && hint.OtherImageId == pair.B;
        var reversedOrder = hint.ImageId == pair.B && hint.OtherImageId == pair.A;
        var applies = hint.Enabled && hint.Type == "tie" && (sameOrder || reversedOrder);
        return applies;
    }
}
