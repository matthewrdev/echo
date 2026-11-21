using System.Diagnostics;
using Echo.Contracts;
using Echo.DenseCloud;
using Echo.Infrastructure;
using Echo.Projects;

namespace Echo.Benchmarks;

public sealed class CloudBenchmark
{
    private readonly ICloudBuilder builder;
    private readonly IProjectStoreFactory stores;
    private readonly IJsonCodec json;
    private readonly IFileOperations files;

    public CloudBenchmark(ICloudBuilder builder, IProjectStoreFactory stores, IJsonCodec json, IFileOperations files)
    {
        this.builder = builder;
        this.stores = stores;
        this.json = json;
        this.files = files;
    }

    public int Run(string[] args)
    {
        if (args.Length < 4 || args.Length > 7)
        {
            Console.Error.WriteLine("Usage: Echo.Benchmarks cloud PROJECT RUN ITERATIONS OUTPUT.ply [WORKERS] [BATCH_ROWS] [DEPTH_MAPS.json]");
            return 2;
        }
        var store = stores.Open(args[0]);
        var run = store.GetRun(args[1]);
        if (args.Length >= 5)
        {
            run.Snapshot.Settings.CloudWorkerThreads = int.Parse(args[4]);
        }
        if (args.Length >= 6)
        {
            run.Snapshot.Settings.CloudBatchRows = int.Parse(args[5]);
        }
        run.Snapshot.Settings.Validate();
        var scene = json.Read<ReconstructionScene>(store.PathFor(run.Artifacts["alignment"] + "/scene.json"));
        List<DepthMap> maps;
        if (args.Length == 7)
        {
            maps = json.Read<List<DepthMap>>(Path.GetFullPath(args[6]));
        }
        else
        {
            var units = json.Read<List<string>>(store.PathFor(run.Artifacts["depth"] + "/units.json"));
            maps = new List<DepthMap>(units.Count);
            foreach (var unit in units)
            {
                maps.Add(json.Read<DepthMap>(store.PathFor(unit + "/depth.json")));
            }
        }
        for (var iteration = 0; iteration < int.Parse(args[2]); iteration++)
        {
            GC.Collect();
            var allocated = GC.GetTotalAllocatedBytes(true);
            var watch = Stopwatch.StartNew();
            var cloud = builder.Build(store, run.Snapshot, scene, maps, default);
            watch.Stop();
            allocated = GC.GetTotalAllocatedBytes(true) - allocated;
            builder.Write(Path.GetFullPath(args[3]), cloud);
            Console.WriteLine(json.Encode(new
            {
                iteration,
                seconds = watch.Elapsed.TotalSeconds,
                allocatedBytes = allocated,
                cameraCount = maps.Count,
                run.Snapshot.Settings.CloudWorkerThreads,
                run.Snapshot.Settings.CloudBatchRows,
                cloud.InputSamples,
                retained = cloud.Samples.Count,
                hash = files.HashFile(Path.GetFullPath(args[3]))
            }));
        }
        return 0;
    }
}
