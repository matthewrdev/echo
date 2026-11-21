using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Echo.Compute;
using Echo.Models;
using Echo.Fusion;
using Echo.Geometry;
using Echo.Infrastructure;
using Echo.Projects;
using Echo.Storage;

namespace Echo.Benchmarks;

public sealed class FusionIntegrationBenchmark
{
    private readonly IProjectStoreFactory stores;
    private readonly IJsonCodec json;
    private readonly ProfileComputeService compute;

    public FusionIntegrationBenchmark(IProjectStoreFactory stores, IJsonCodec json, IComputeService compute)
    {
        this.stores = stores;
        this.json = json;
        this.compute = new ProfileComputeService(compute);
    }

    public int Run(string[] args)
    {
        if (args.Length is < 7 or > 8)
        {
            throw new EchoException("Usage: fusion-integration PROJECT RUN GRID_FILE AXIS VOXEL CELL_LIMIT MODE (Reference, Cpu, Metal, MetalTiled) [KEEP_EVERY_N].", 2);
        }
        var axis = Enum.Parse<GridAxis>(args[3], true);
        var voxel = double.Parse(args[4], CultureInfo.InvariantCulture);
        var limit = int.Parse(args[5], CultureInfo.InvariantCulture);
        var mode = Enum.Parse<FusionIntegrationBenchmarkMode>(args[6], true);
        var valid = Enum.IsDefined(axis) && Enum.IsDefined(mode) && limit is > 0 and <= 262144;
        if (!valid || !double.IsFinite(voxel) || voxel <= 0)
        {
            throw new EchoException("Use a valid axis/mode, positive voxel size and 1–262144 cells.", 2);
        }
        var store = stores.Open(args[0]);
        var run = store.GetRun(args[1]);
        using var data = new BulkDataSession(512L * 1024 * 1024, 0);
        var scene = data.Read<ReconstructionScene>(store.PathFor(run.Artifacts["alignment"] + "/scene.json"));
        var units = json.Read<List<string>>(store.PathFor(run.Artifacts["depth"] + "/units.json"));
        var photos = run.Snapshot.Images.ToDictionary(photo => photo.Id, StringComparer.Ordinal);
        var cameras = scene.Cameras.ToDictionary(camera => camera.ImageId, StringComparer.Ordinal);
        var views = units.Select(unit => data.Read<DepthMap>(store.PathFor(unit + "/depth.json")))
            .Select(map => new DepthView(photos[map.ImageId], cameras[map.ImageId], map)).ToArray();
        var source = data.Open(args[2], new GridSpanRecordCodec());
        var spans = new GridSpan[Math.Min(source.Count, limit)];
        source.ReadRange((source.Count - spans.Length) / 2, spans);
        var cells = new Grid[limit];
        var count = new FusionGridReader(spans, axis).Read(cells);
        Array.Resize(ref cells, count);
        var stride = args.Length == 8 ? int.Parse(args[7], CultureInfo.InvariantCulture) : 1;
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, 1);
        for (var index = 0; index < cells.Length; index++)
        {
            if (index % stride != 0)
            {
                cells[index] = new Grid(int.MaxValue / 2, int.MaxValue / 2, int.MaxValue / 2);
            }
        }
        using var scope = compute.BeginScope(mode == FusionIntegrationBenchmarkMode.Metal ? 3L << 30 : 512L << 20);
        var plan = FusionIntegrationPlanning.Resolve(8L << 30, compute.MemoryBudgetBytes, count,
            mode == FusionIntegrationBenchmarkMode.Metal ? views.Sum(view => (long)view.Map.SampleCount) : long.MaxValue);
        var preparation = Stopwatch.StartNew();
        using var integration = mode == FusionIntegrationBenchmarkMode.Reference ? null
            : new FusionBatchIntegrator(views, voxel, run.Snapshot.Settings.CloudDepthTolerance, plan, compute,
                mode is FusionIntegrationBenchmarkMode.Metal or FusionIntegrationBenchmarkMode.MetalTiled ? ComputeBackend.Metal : ComputeBackend.Cpu, default);
        preparation.Stop();
        cells.CopyTo(integration?.Cells ?? cells, 0);
        IList<VolumeSample> Execute() => integration == null
            ? FusionIntegrationReference.Integrate(cells, views, voxel, run.Snapshot.Settings.CloudDepthTolerance)
            : integration.Integrate(count);
        var iterations = mode is FusionIntegrationBenchmarkMode.Metal or FusionIntegrationBenchmarkMode.MetalTiled ? 16 : 1;
        for (var warmup = 0; warmup < 5; warmup++)
        {
            Execute();
        }
        for (var repeat = 0; repeat < 7; repeat++)
        {
            var uploaded = compute.UploadedBytes;
            var readback = compute.ReadbackBytes;
            var allocated = GC.GetTotalAllocatedBytes(true);
            var read = data.Statistics.ReadBytes;
            var watch = Stopwatch.StartNew();
            var samples = Execute();
            for (var iteration = 1; iteration < iterations; iteration++)
            {
                samples = Execute();
            }
            watch.Stop();
            var measurement = new FusionIntegrationMeasurement(mode, count, samples.Count, watch.Elapsed.TotalSeconds / iterations,
                (GC.GetTotalAllocatedBytes(false) - allocated) / iterations, (data.Statistics.ReadBytes - read) / iterations, scope.Devices.Length > 0,
                samples.Sum(sample => (long)sample.Views), samples.Sum(sample => (double)sample.Distance),
                preparation.Elapsed.TotalSeconds, plan.ResidentDepthCount, HashSamples(samples), (compute.UploadedBytes - uploaded) / iterations,
                (compute.ReadbackBytes - readback) / iterations);
            Console.WriteLine(JsonSerializer.Serialize(measurement, FusionPlanningBenchmarkJsonContext.Default.FusionIntegrationMeasurement));
        }
        return 0;
    }

    private string HashSamples(IList<VolumeSample> samples)
    {
        var codec = new VolumeRecordCodec();
        var bytes = new byte[4096 * codec.Size];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var first = 0; first < samples.Count; first += 4096)
        {
            var count = Math.Min(4096, samples.Count - first);
            for (var index = 0; index < count; index++)
            {
                codec.Encode(samples[first + index], bytes.AsSpan(index * codec.Size, codec.Size));
            }
            hash.AppendData(bytes.AsSpan(0, count * codec.Size));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

}
