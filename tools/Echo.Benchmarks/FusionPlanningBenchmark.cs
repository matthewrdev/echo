using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Text.Json;
using Echo.Contracts;
using Echo.DenseCloud;
using Echo.Fusion;
using Echo.Geometry;
using Echo.Infrastructure;
using Echo.Projects;
using Echo.Storage;

namespace Echo.Benchmarks;

[Export]
public sealed class FusionPlanningBenchmark
{
    private readonly IProjectStoreFactory stores;
    private readonly IJsonCodec json;
    private readonly ICloudBuilder clouds;

    [ImportingConstructor]
    public FusionPlanningBenchmark(IProjectStoreFactory stores, IJsonCodec json, ICloudBuilder clouds)
    {
        this.stores = stores;
        this.json = json;
        this.clouds = clouds;
    }

    public int Run(string[] args)
    {
        if (args.Length != 5)
        {
            throw new EchoException("Usage: fusion-planning PROJECT RUN POINT_LIMIT WORKERS NEW_OUTPUT_DIRECTORY (zero limit/workers uses all/auto).", 2);
        }
        var store = stores.Open(args[0]);
        var run = store.GetRun(args[1]);
        var pointLimit = int.Parse(args[2]);
        var workers = int.Parse(args[3]);
        var directory = Path.GetFullPath(args[4]);
        if (pointLimit < 0 || workers < 0 || Directory.Exists(directory))
        {
            throw new EchoException("Use nonnegative limits and a new benchmark output directory.", 2);
        }
        Directory.CreateDirectory(directory);
        using var session = new BulkDataSession(64 * 1024 * 1024, 0);
        using var work = new WorkBuffers(session, directory, 64 * 1024 * 1024, 0);
        var scene = session.Read<ReconstructionScene>(store.PathFor(run.Artifacts["alignment"] + "/scene.json"));
        var units = json.Read<List<string>>(store.PathFor(run.Artifacts["depth"] + "/units.json"));
        var photos = run.Snapshot.Images.ToDictionary(photo => photo.Id, StringComparer.Ordinal);
        var cameras = scene.Cameras.ToDictionary(camera => camera.ImageId, StringComparer.Ordinal);
        var views = units.Select(unit => session.Read<DepthMap>(store.PathFor(unit + "/depth.json")))
            .Select(map => new DepthView(photos[map.ImageId], cameras[map.ImageId], map)).ToArray();
        var path = run.Artifacts["cloud"];
        var cloud = clouds.Read(store.PathFor(path + "/cloud.ply"), json.Read<DenseCloudReport>(store.PathFor(path + "/cloud.json")), session);
        var footprint = FusionFootprint.Measure(views, cloud, default);
        if (pointLimit > 0 && pointLimit < cloud.Samples.Count)
        {
            var subset = new DenseSample[pointLimit];
            ((PagedList<DenseSample>)cloud.Samples).ReadRange(0, subset);
            cloud.Samples = subset;
        }
        var resources = FusionMemoryBudget.Resolve(run.Snapshot.Settings.FusionMemoryBudgetMb, 32L * 1024 * 1024 * 1024);
        var options = FusionGridPlanning.Resolve(resources, cloud.Samples.Count);
        if (workers > 0)
        {
            options = options with
            {
                Workers = workers
            };
        }
        var settings = run.Snapshot.Settings;
        var voxel = settings.FusionVoxelSize > 0 ? settings.FusionVoxelSize : footprint * .8 / Math.Sqrt(settings.FusionDetailMultiplier);
        var byId = views.ToDictionary(view => view.Map.ImageId, StringComparer.Ordinal);
        var sources = cloud.ImageIds.Select(id => byId.GetValueOrDefault(id)).ToArray();
        options = options with
        {
            Axis = FusionGridAxes.Select(cloud, sources, default)
        };
        var watch = Stopwatch.StartNew();
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        var allocated = GC.GetTotalAllocatedBytes(true);
        var status = new FusionPlanningProgress(update => Console.Error.WriteLine(JsonSerializer.Serialize(
            new FusionPlanningProgressRecord(watch.Elapsed.TotalSeconds, update), FusionPlanningBenchmarkJsonContext.Default.FusionPlanningProgressRecord)));
        var keys = FusionCandidatePlanner.Build(cloud, sources, settings.CloudDepthTolerance, voxel, long.MaxValue,
            options, default, work, status)!;
        var seconds = watch.Elapsed.TotalSeconds;
        process.Refresh();
        var measurement = new FusionPlanningMeasurement(cloud.Samples.Count, options.Workers, options.BatchCapacity,
            options.Axis, voxel, keys.Count, seconds, (process.TotalProcessorTime - cpu).TotalSeconds,
            GC.GetTotalAllocatedBytes(false) - allocated, process.WorkingSet64, session.Statistics, work.IndexDiskBytes);
        Console.WriteLine(JsonSerializer.Serialize(measurement, FusionPlanningBenchmarkJsonContext.Default.FusionPlanningMeasurement));
        return 0;
    }
}
