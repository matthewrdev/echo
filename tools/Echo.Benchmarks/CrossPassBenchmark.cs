using System.ComponentModel.Composition;
using System.Diagnostics;
using Echo.Contracts;
using Echo.Features;
using Echo.Geometry;
using Echo.Imaging;
using Echo.Infrastructure;
using Echo.Matching;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Benchmarks;

[Export]
public sealed class CrossPassBenchmark
{
    private readonly IScaleSpaceFeatures scaleFeatures;
    private readonly IFeatureExtractor legacyFeatures;
    private readonly IRasterImageService images;
    private readonly IFeatureMatcher matcher;
    private readonly ICameraGeometry geometry;
    private readonly IGuidedFeatureMatcher guided;
    private readonly IProjectStoreFactory stores;
    private readonly IFileOperations files;
    private readonly IJsonCodec json;

    [ImportingConstructor]
    public CrossPassBenchmark(IScaleSpaceFeatures scaleFeatures, IFeatureExtractor legacyFeatures, IRasterImageService images, IFeatureMatcher matcher, ICameraGeometry geometry, IProjectStoreFactory stores, IFileOperations files, IJsonCodec json, IGuidedFeatureMatcher guided)
    {
        this.scaleFeatures = scaleFeatures;
        this.legacyFeatures = legacyFeatures;
        this.images = images;
        this.matcher = matcher;
        this.geometry = geometry;
        this.stores = stores;
        this.files = files;
        this.json = json;
        this.guided = guided;
    }

    public int Run(string[] args)
    {
        if (args.Length < 6)
        {
            Console.Error.WriteLine("Usage: Echo.Benchmarks cross-pass PROJECT OUTPUT_DIRECTORY WIDTH LIMIT LEFT_NAME RIGHT_NAME [RIGHT_NAME...]");
            return 2;
        }
        var store = stores.Open(args[0]);
        var project = store.Load();
        var directory = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(directory);
        var settings = new Settings { ScaleInvariantFeatures = false, FeatureWidth = int.Parse(args[2]), FeatureLimit = int.Parse(args[3]), DetectLines = false };
        settings.Validate();
        var photos = new List<Photo>();
        for (var index = 4; index < args.Length; index++)
        {
            photos.Add(project.Images.Single(photo => photo.Name == args[index]));
        }
        var results = new List<object>();
        foreach (var method in new[] { "legacy", "scale-space" })
        {
            var features = new List<FeatureSet>();
            GC.Collect();
            var allocated = GC.GetTotalAllocatedBytes(true);
            var watch = Stopwatch.StartNew();
            foreach (var photo in photos)
            {
                FeatureSet set;
                if (method == "legacy")
                {
                    set = legacyFeatures.Extract(store, photo, settings, default);
                }
                else
                {
                    using var bitmap = SKBitmap.Decode(store.PathFor(photo.Source));
                    var gray = images.ResizeGray(bitmap, settings.FeatureWidth);
                    set = new FeatureSet { ImageId = photo.Id, Features = scaleFeatures.Detect(gray.Pixels, gray.Width, gray.Height, settings.FeatureLimit, default) };
                    foreach (var feature in set.Features)
                    {
                        feature.X *= (double)photo.Width / gray.Width;
                        feature.Y *= (double)photo.Height / gray.Height;
                    }
                }
                features.Add(set);
                files.AtomicJson(Path.Combine(directory, method + "-" + photo.Id + ".json"), set);
            }
            var extractionSeconds = watch.Elapsed.TotalSeconds;
            var extractionBytes = GC.GetTotalAllocatedBytes(true) - allocated;
            for (var index = 1; index < photos.Count; index++)
            {
                watch.Restart();
                var pair = matcher.Match(features[0], features[index], settings, default);
                var matchingSeconds = watch.Elapsed.TotalSeconds;
                watch.Restart();
                var relative = geometry.EstimateRelative(photos[0], photos[index], pair.Matches);
                var verified = FundamentalMatrixEstimator.Estimate(pair.Matches, 4, default);
                var recovered = guided.Recover(features[0], features[index], pair.Matches, settings, 4, default);
                var result = new
                {
                    method,
                    settings.FeatureWidth,
                    settings.FeatureLimit,
                    left = photos[0].Name,
                    right = photos[index].Name,
                    leftFeatures = features[0].Features.Count,
                    rightFeatures = features[index].Features.Count,
                    candidates = pair.Matches.Count,
                    inliers = relative?.Inliers.Count ?? 0,
                    fundamentalInliers = verified?.Inliers.Count ?? 0,
                    robustIterations = verified?.Iterations ?? 0,
                    recoveredMatches = recovered?.Matches.Count ?? 0,
                    extractionSeconds,
                    extractionBytes,
                    matchingSeconds,
                    verificationSeconds = watch.Elapsed.TotalSeconds
                };
                files.AtomicJson(Path.Combine(directory, method + "-pair-" + photos[index].Id + ".json"), pair);
                files.AtomicJson(Path.Combine(directory, method + "-verified-" + photos[index].Id + ".json"), relative);
                files.AtomicJson(Path.Combine(directory, method + "-fundamental-" + photos[index].Id + ".json"), verified);
                files.AtomicJson(Path.Combine(directory, method + "-recovered-" + photos[index].Id + ".json"), recovered);
                results.Add(result);
                Console.WriteLine(json.Encode(result));
            }
        }
        files.AtomicJson(Path.Combine(directory, "metrics.json"), results);
        return 0;
    }
}
