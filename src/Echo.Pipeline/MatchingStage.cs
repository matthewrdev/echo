using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Matching;

namespace Echo.Pipeline;

[Export(typeof(IPipelineStage)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class MatchingStage : IPipelineStage
{
    private readonly IFileOperations fileOperations;
    private readonly IJsonCodec jsonCodec;
    private readonly IFeatureMatcher featureMatcher;
    private readonly IImagePairSelector pairSelector;

    [ImportingConstructor]
    public MatchingStage(IImagePairSelector pairSelector, IFileOperations fileOperations, IJsonCodec jsonCodec, IFeatureMatcher featureMatcher)
    {
        this.fileOperations = fileOperations;
        this.jsonCodec = jsonCodec;
        this.featureMatcher = featureMatcher;
        this.pairSelector = pairSelector;
    }

    public string Name => "matching";
    public int Order => 1;

    public void Execute(RunContext context)
    {
        var project = context.Run.Snapshot;
        var images = context.Images;
        var store = context.Store;
        var cancellation = context.Cancellation;
        var featureUnits = context.ReadArtifact<Dictionary<string, string>>("features", "units.json");
        var wanted = pairSelector.Select(project, images, featureUnits);
        context.ReportProgress("matching", $"Selected {wanted.Count:N0} image pairs using viewing overlap, spatial neighbours, capture sequence and hints.");

        var pairUnits = new List<string>();
        // Bounded cache: features are small but do not retain every decoded source image.
        foreach (var pair in wanted)
        {
            var hints = project.Hints.Where(hint => AppliesToPair(hint, pair)).ToArray();
            var unit = context.Build("match-pair", new
            {
                a = featureUnits[pair.A],
                b = featureUnits[pair.B],
                hints,
                matcherVersion = 3,
                project.Settings.MatchRatioThreshold,
                project.Settings.MatchDistanceThreshold
            }, dir =>
                        {
                            var a = jsonCodec.Read<FeatureSet>(store.PathFor(featureUnits[pair.A] + "/features.json"));
                            var b = jsonCodec.Read<FeatureSet>(store.PathFor(featureUnits[pair.B] + "/features.json"));
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

                            fileOperations.AtomicJson(Path.Combine(dir, "pair.json"), matches);
                        });
            pairUnits.Add(unit);
            context.Report("pairCheckpoint", new
            {
                stage = "matching",
                pair.A,
                pair.B,
                completed = pairUnits.Count,
                total = wanted.Count
            });
        }

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
    private bool AppliesToPair(Hint hint, PairIdentity pair)
    {
        var sameOrder = hint.ImageId == pair.A && hint.OtherImageId == pair.B;
        var reversedOrder = hint.ImageId == pair.B && hint.OtherImageId == pair.A;
        var applies = hint.Enabled && hint.Type == "tie" && (sameOrder || reversedOrder);
        return applies;
    }
}
