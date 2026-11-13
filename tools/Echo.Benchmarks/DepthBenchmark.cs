using System.ComponentModel.Composition;
using System.Diagnostics;
using Echo.Contracts;
using Echo.Depth;
using Echo.Infrastructure;
using Echo.Projects;

namespace Echo.Benchmarks;

[Export]
public sealed class DepthBenchmark
{
    private readonly IDepthEstimator estimator;
    private readonly IProjectStoreFactory stores;
    private readonly IJsonCodec json;
    private readonly IFileOperations files;
    private readonly IDepthExecutionPlanner executionPlanner;

    [ImportingConstructor]
    public DepthBenchmark(IDepthEstimator estimator, IProjectStoreFactory stores, IJsonCodec json, IFileOperations files, IDepthExecutionPlanner executionPlanner)
    {
        this.estimator = estimator;
        this.stores = stores;
        this.json = json;
        this.files = files;
        this.executionPlanner = executionPlanner;
    }

    public int Run(string[] args)
    {
        if (args.Length < 5 || args.Length > 8)
        {
            Console.Error.WriteLine("Usage: Echo.Benchmarks PROJECT RUN WIDTH ITERATIONS OUTPUT.json [CAMERAS] [CONCURRENCY] [FIRST_IMAGE_NAME]");
            return 2;
        }

        var store = stores.Open(args[0]);
        var run = store.GetRun(args[1]);
        var scene = json.Read<ReconstructionScene>(store.PathFor(run.Artifacts["alignment"] + "/scene.json"));
        run.Snapshot.Settings.DepthWidth = int.Parse(args[2]);
        var cameraCount = args.Length >= 6 ? Math.Min(int.Parse(args[5]), scene.Cameras.Count) : 1;
        if (args.Length >= 7)
        {
            run.Snapshot.Settings.DepthConcurrency = int.Parse(args[6]);
        }
        var plan = executionPlanner.Plan(run.Snapshot, cameraCount);
        var firstCamera = 0;
        if (args.Length == 8)
        {
            var image = run.Snapshot.Images.Single(photo => photo.Name == args[7]);
            firstCamera = scene.Cameras.FindIndex(camera => camera.ImageId == image.Id);
            if (firstCamera < 0 || firstCamera + cameraCount > scene.Cameras.Count)
            {
                throw new ArgumentException("Requested image range is not present in the saved alignment.");
            }
        }
        for (var iteration = 0; iteration < int.Parse(args[3]); iteration++)
        {
            GC.Collect();
            var allocated = GC.GetTotalAllocatedBytes(true);
            var watch = Stopwatch.StartNew();
            var maps = new DepthMap[cameraCount];
            var phases = new List<DepthLevelMetrics>[cameraCount];
            Parallel.For(0, cameraCount, new ParallelOptions { MaxDegreeOfParallelism = plan.ConcurrentImages }, index =>
            {
                var profile = new List<DepthLevelMetrics>();
                phases[index] = profile;
                maps[index] = estimator.Estimate(store, run.Snapshot, scene, scene.Cameras[firstCamera + index], default, plan.WorkersPerImage, metrics: profile.Add);
            });
            watch.Stop();
            allocated = GC.GetTotalAllocatedBytes(true) - allocated;
            var writeStarted = Stopwatch.GetTimestamp();
            var writeAllocated = GC.GetTotalAllocatedBytes(true);
            files.AtomicJson(Path.GetFullPath(args[4]), cameraCount == 1 ? (object)maps[0] : maps);
            var writeSeconds = Stopwatch.GetElapsedTime(writeStarted).TotalSeconds;
            writeAllocated = GC.GetTotalAllocatedBytes(true) - writeAllocated;
            Console.WriteLine(json.Encode(new
            {
                iteration,
                seconds = watch.Elapsed.TotalSeconds,
                allocatedBytes = allocated,
                cameraCount,
                plan.ConcurrentImages,
                plan.WorkersPerImage,
                writeSeconds,
                writeAllocatedBytes = writeAllocated,
                phases,
                valid = maps.Sum(map => map.Depth.Count(d => d > 0)),
                hash = files.Hash(cameraCount == 1 ? (object)maps[0] : maps)
            }));
        }
        return 0;
    }
}
