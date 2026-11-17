using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Echo.Compute;
using Echo.Contracts;
using Echo.DenseCloud;
using Echo.Fusion;
using Echo.Infrastructure;
using Echo.Projects;
using Echo.Storage;

namespace Echo.Benchmarks;

[Export]
public sealed class FusionResumeBenchmark
{
    private readonly IProjectStoreFactory stores;
    private readonly IJsonCodec json;
    private readonly ICloudBuilder clouds;
    private readonly ProfileComputeService compute;

    [ImportingConstructor]
    public FusionResumeBenchmark(IProjectStoreFactory stores, IJsonCodec json, ICloudBuilder clouds, IComputeService compute)
    {
        this.stores = stores;
        this.json = json;
        this.clouds = clouds;
        this.compute = new ProfileComputeService(compute);
    }

    public int Run(string[] args)
    {
        if (args.Length != 5)
        {
            throw new EchoException("Usage: fusion-resume PROJECT RUN POINT_LIMIT VOXEL Cpu|Metal. Profiles cold integration and six cached resumes using in-memory checkpoint storage.", 2);
        }
        var store = stores.Open(args[0]);
        var run = store.GetRun(args[1]);
        var limit = int.Parse(args[2], CultureInfo.InvariantCulture);
        var voxel = double.Parse(args[3], CultureInfo.InvariantCulture);
        var backend = Enum.Parse<ComputeBackend>(args[4], true);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        run.Snapshot.Settings.FusionVoxelSize = voxel;
        run.Snapshot.Settings.ComputeBackend = backend;
        run.Snapshot.Settings.Validate();
        using var data = new BulkDataSession(256L << 20, 0);
        var scene = data.Read<ReconstructionScene>(store.PathFor(run.Artifacts["alignment"] + "/scene.json"));
        var units = json.Read<List<string>>(store.PathFor(run.Artifacts["depth"] + "/units.json"));
        var maps = units.Select(unit => data.Read<DepthMap>(store.PathFor(unit + "/depth.json"))).ToArray();
        var path = run.Artifacts["cloud"];
        var cloud = clouds.Read(store.PathFor(path + "/cloud.ply"), json.Read<DenseCloudReport>(store.PathFor(path + "/cloud.json")), data);
        var samples = new DenseSample[Math.Min(limit, cloud.Samples.Count)];
        RecordListOperations.Read(cloud.Samples, 0, samples.AsSpan());
        cloud.Samples = samples;
        var checkpoints = new FusionProfileCheckpoints();
        var fusion = new DepthFusion(compute);
        using var scope = compute.BeginScope(3L << 30);
        var resources = new FusionResourcePlan(8L << 30, int.MaxValue);
        var directory = Path.Combine(Path.GetTempPath(), "echo-fusion-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            for (var iteration = 0; iteration < 7; iteration++)
            {
                using var work = new WorkBuffers(data, directory);
                var before = data.Statistics;
                var uploaded = compute.UploadedBytes;
                var readback = compute.ReadbackBytes;
                var allocated = GC.GetTotalAllocatedBytes(true);
                var watch = Stopwatch.StartNew();
                var volume = fusion.Fuse(run.Snapshot, scene, maps, cloud, resources, default,
                    work: work, chunks: checkpoints, grids: checkpoints);
                watch.Stop();
                var allocation = GC.GetTotalAllocatedBytes(false) - allocated;
                using var process = Process.GetCurrentProcess();
                var measurement = new FusionResumeMeasurement(iteration, samples.Length, volume.Samples.Count,
                    watch.Elapsed.TotalSeconds, allocation, data.Statistics.ReadBytes - before.ReadBytes,
                    data.Statistics.WrittenBytes - before.WrittenBytes, compute.UploadedBytes - uploaded,
                    compute.ReadbackBytes - readback, process.PeakWorkingSet64, checkpoints.Builds, checkpoints.GridBuilds, Hash(volume.Samples));
                Console.WriteLine(JsonSerializer.Serialize(measurement, FusionPlanningBenchmarkJsonContext.Default.FusionResumeMeasurement));
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        return 0;
    }

    private string Hash(IList<VolumeSample> samples)
    {
        var codec = new VolumeRecordCodec();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var records = new VolumeSample[4096];
        var bytes = new byte[4096 * codec.Size];
        for (var first = 0; first < samples.Count; first += records.Length)
        {
            var count = Math.Min(records.Length, samples.Count - first);
            RecordListOperations.Read(samples, first, records.AsSpan(0, count));
            codec.EncodeBlock(records.AsSpan(0, count), bytes.AsSpan(0, count * codec.Size));
            hash.AppendData(bytes.AsSpan(0, count * codec.Size));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
