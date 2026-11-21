using System.Diagnostics;
using Echo.Contracts;
using Echo.Features;
using Echo.Infrastructure;
using Echo.Projects;

namespace Echo.Benchmarks;

public sealed class FeatureBenchmark
{
    private readonly IFeatureExtractor extractor;
    private readonly IProjectStoreFactory stores;
    private readonly IJsonCodec json;
    private readonly IFileOperations files;

    public FeatureBenchmark(IFeatureExtractor extractor, IProjectStoreFactory stores, IJsonCodec json, IFileOperations files)
    {
        this.extractor = extractor;
        this.stores = stores;
        this.json = json;
        this.files = files;
    }

    public int Run(string[] args)
    {
        if (args.Length < 5 || args.Length > 7)
        {
            Console.Error.WriteLine("Usage: Echo.Benchmarks features PROJECT WIDTH FEATURE_LIMIT ITERATIONS OUTPUT.json [PHOTOS] [DETECT_LINES]");
            return 2;
        }

        var store = stores.Open(args[0]);
        var project = store.Load();
        project.Settings.FeatureWidth = int.Parse(args[1]);
        project.Settings.FeatureLimit = int.Parse(args[2]);
        var iterations = int.Parse(args[3]);
        var photoCount = args.Length >= 6 ? int.Parse(args[5]) : 1;
        if (args.Length == 7)
        {
            project.Settings.DetectLines = bool.Parse(args[6]);
        }
        project.Settings.Validate();
        var photos = project.Images.Where(photo => photo.Enabled).Take(photoCount).ToArray();
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            GC.Collect();
            var allocated = GC.GetTotalAllocatedBytes(true);
            var watch = Stopwatch.StartNew();
            var results = new FeatureSet[photos.Length];
            for (var index = 0; index < photos.Length; index++)
            {
                results[index] = extractor.Extract(store, photos[index], project.Settings, default);
            }
            watch.Stop();
            allocated = GC.GetTotalAllocatedBytes(true) - allocated;
            Console.WriteLine(json.Encode(new
            {
                iteration,
                seconds = watch.Elapsed.TotalSeconds,
                allocatedBytes = allocated,
                photoCount = photos.Length,
                project.Settings.FeatureWidth,
                project.Settings.FeatureLimit,
                project.Settings.DetectLines,
                points = results.Sum(result => result.Features.Count),
                lines = results.Sum(result => result.Lines.Count),
                hash = files.Hash(results)
            }));
            files.AtomicJson(Path.GetFullPath(args[4]), results);
        }
        return 0;
    }
}
