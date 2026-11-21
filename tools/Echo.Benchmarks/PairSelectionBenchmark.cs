using System.Diagnostics;
using Echo.Geometry;
using Echo.Infrastructure;
using Echo.Matching;
using Echo.Models;
using Echo.Pipeline.Matching;
using Echo.Pipeline;
using Echo.Projects;

namespace Echo.Benchmarks;

public sealed class PairSelectionBenchmark
{
    private readonly IImagePairSelector selector;
    private readonly IFeatureMatcher matcher;
    private readonly IProjectStoreFactory stores;
    private readonly IJsonCodec json;
    private readonly IFileOperations files;

    public PairSelectionBenchmark(IImagePairSelector selector, IFeatureMatcher matcher, IProjectStoreFactory stores, IJsonCodec json, IFileOperations files)
    {
        this.selector = selector;
        this.matcher = matcher;
        this.stores = stores;
        this.json = json;
        this.files = files;
    }

    public int Run(string[] args)
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine("Usage: Echo.Benchmarks pairs PROJECT RUN OUTPUT.json FOCUS_IMAGE_NAME");
            return 2;
        }
        var store = stores.Open(args[0]);
        var run = json.Read<RunData>(store.PathFor($"runs/{args[1]}/run.json"));
        var project = run.Snapshot;
        var images = project.Images.Where(photo => photo.Enabled).OrderBy(photo => photo.CapturedAt).ThenBy(photo => photo.Name).ToArray();
        var featureUnits = json.Read<Dictionary<string, string>>(store.PathFor(run.Artifacts["features"] + "/units.json"));
        var savedUnits = json.Read<List<string>>(store.PathFor(run.Artifacts["matching"] + "/units.json"));
        var savedPairs = new Dictionary<string, ImagePair>();
        foreach (var path in savedUnits)
        {
            var pair = json.Read<ImagePair>(store.PathFor(path + "/pair.json"));
            savedPairs.Add(pair.A + ":" + pair.B, pair);
        }
        var watch = Stopwatch.StartNew();
        var selected = selector.Select(project, images, featureUnits);
        watch.Stop();
        var focus = images.Single(photo => photo.Name == args[3]);
        var photos = images.ToDictionary(photo => photo.Id);
        var checks = new List<object>();
        foreach (var pair in selected)
        {
            if (pair.A != focus.Id && pair.B != focus.Id)
            {
                continue;
            }
            var key = pair.A + ":" + pair.B;
            var alreadyTested = savedPairs.TryGetValue(key, out var matches);
            matches ??= matcher.Match(
                json.Read<FeatureSet>(store.PathFor(featureUnits[pair.A] + "/features.json")),
                json.Read<FeatureSet>(store.PathFor(featureUnits[pair.B] + "/features.json")), project.Settings, default);
            var relative = CameraGeometry.EstimateRelative(photos[pair.A], photos[pair.B], matches.Matches);
            var other = photos[pair.A == focus.Id ? pair.B : pair.A];
            checks.Add(new
            {
                other.Name,
                other.Gps,
                other.Attitude,
                alreadyTested,
                candidateMatches = matches.Matches.Count,
                geometricallyVerified = relative?.Inliers.Count ?? 0
            });
        }
        var result = new
        {
            focus.Name,
            savedPairCount = savedPairs.Count,
            selectedPairCount = selected.Count,
            newPairCount = selected.Count(pair => !savedPairs.ContainsKey(pair.A + ":" + pair.B)),
            selectionSeconds = watch.Elapsed.TotalSeconds,
            checks
        };
        files.AtomicJson(Path.GetFullPath(args[2]), result);
        Console.WriteLine(json.Encode(result));
        return 0;
    }
}
