using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Echo.Compute;
using Echo.Contracts;
using Echo.Fusion;
using Echo.Geometry;
using Echo.Infrastructure;
using Echo.Projects;
using Echo.Storage;

namespace Echo.Benchmarks;

[Export]
public sealed class FusionIntegrationBenchmark
{
    private readonly IProjectStoreFactory stores;
    private readonly IJsonCodec json;
    private readonly IComputeService compute;

    [ImportingConstructor]
    public FusionIntegrationBenchmark(IProjectStoreFactory stores, IJsonCodec json, IComputeService compute)
    {
        this.stores = stores;
        this.json = json;
        this.compute = compute;
    }

    public int Run(string[] args)
    {
        if (args.Length != 7)
        {
            throw new EchoException("Usage: fusion-integration PROJECT RUN GRID_FILE AXIS VOXEL CELL_LIMIT MODE (Reference, Cpu, Metal).", 2);
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
        using var scope = compute.BeginScope(512L * 1024 * 1024);
        var plan = FusionIntegrationPlanning.Resolve(4L * 1024 * 1024 * 1024, compute.MemoryBudgetBytes);
        using var integration = mode == FusionIntegrationBenchmarkMode.Reference ? null
            : new FusionBatchIntegrator(views, voxel, run.Snapshot.Settings.CloudDepthTolerance, plan, compute,
                mode == FusionIntegrationBenchmarkMode.Metal ? ComputeBackend.Metal : ComputeBackend.Cpu, default);
        cells.CopyTo(integration?.Cells ?? cells, 0);
        IList<VolumeSample> Execute() => integration == null
            ? FusionIntegrationReference.Integrate(cells, views, voxel, run.Snapshot.Settings.CloudDepthTolerance)
            : integration.Integrate(count);
        Execute();
        for (var repeat = 0; repeat < 3; repeat++)
        {
            var allocated = GC.GetTotalAllocatedBytes(true);
            var read = data.Statistics.ReadBytes;
            var watch = Stopwatch.StartNew();
            var samples = Execute();
            watch.Stop();
            var measurement = new FusionIntegrationMeasurement(mode, count, samples.Count, watch.Elapsed.TotalSeconds,
                GC.GetTotalAllocatedBytes(false) - allocated, data.Statistics.ReadBytes - read, scope.Devices.Length > 0,
                samples.Sum(sample => (long)sample.Views), samples.Sum(sample => (double)sample.Distance));
            Console.WriteLine(JsonSerializer.Serialize(measurement, FusionPlanningBenchmarkJsonContext.Default.FusionIntegrationMeasurement));
        }
        return 0;
    }
}
