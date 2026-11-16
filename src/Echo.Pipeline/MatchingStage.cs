using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Compute;
using Echo.Infrastructure;
using Echo.Matching;
using Echo.Geometry;

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

    [ImportingConstructor]
    public MatchingStage(IImagePairSelector pairSelector, IFileOperations fileOperations, IFeatureMatcher featureMatcher, IImageWorkPlanner workPlanner, IOrderedWorkExecutor workExecutor, IFeatureSetCacheFactory cacheFactory, [Import(AllowDefault = true)] IComputeService? compute = null)
    {
        this.fileOperations = fileOperations;
        this.featureMatcher = featureMatcher;
        this.pairSelector = pairSelector;
        this.workPlanner = workPlanner;
        this.workExecutor = workExecutor;
        this.cacheFactory = cacheFactory;
        this.compute = compute;
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
        var photos = new Dictionary<string, Photo>(images.Count, StringComparer.Ordinal);
        foreach (var photo in images)
        {
            photos.Add(photo.Id, photo);
        }
        context.ReportProgress("matching", $"Selected {wanted.Count:N0} image pairs using viewing overlap, spatial neighbours, capture sequence and hints.");

        var pairUnits = new List<string>();
        var plan = workPlanner.Matching(project.Settings, wanted.Count);
        using var cache = cacheFactory.Create(plan.FeatureCacheBytes);
        workExecutor.Run(wanted.Count, plan.Workers, index => BuildPair(context, wanted[index], photos, featureUnits, cache), (index, unit) =>
        {
            var pair = wanted[index];
            pairUnits.Add(unit);
            context.Report("pairCheckpoint", new
            {
                stage = "matching",
                pair.A,
                pair.B,
                completed = pairUnits.Count,
                total = wanted.Count
            });
        }, cancellation);

        context.Publish("matching", context.Build("matching", new
        {
            selectionVersion = 2,
            pairUnits
        }, directory =>
        {
            fileOperations.AtomicJson(Path.Combine(directory, "units.json"), pairUnits);
            fileOperations.AtomicJson(Path.Combine(directory, "selection.json"), new
            {
                selectionVersion = 2,
                project.Settings.FrustumPairNeighbours,
                project.Settings.PairSearchDistance,
                pairs = wanted
            });
        }));
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
            context.ComputeImplementation,
            project.Settings.MatchRatioThreshold,
            project.Settings.MatchDistanceThreshold
        }, directory =>
        {
            var a = cache.Read(context.Store.PathFor(featureUnits[pair.A] + "/features.json"), cancellation);
            var b = cache.Read(context.Store.PathFor(featureUnits[pair.B] + "/features.json"), cancellation);
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

            ImagePairVerifier.Verify(photos[pair.A], photos[pair.B], matches, cancellation, compute, project.Settings.ComputeBackend);
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
