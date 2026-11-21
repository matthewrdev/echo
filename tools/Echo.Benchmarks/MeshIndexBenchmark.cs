using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Echo.Geometry;
using Echo.Meshing.Geometry;
using Echo.Meshing;
using Echo.Models;
using Echo.Storage.Artifacts;
using Echo.Storage.Resources;
using Echo.Storage;

namespace Echo.Benchmarks;

public sealed class MeshIndexBenchmark
{
    public MeshIndexBenchmark()
    {
    }

    public int Run(string[] args)
    {
        if (args.Length != 2)
        {
            throw new EchoException("Usage: mesh-index GRID_SIDE CACHE_MIB. Profiles deterministic curved-grid construction and 2,000 nearest-surface queries.", 2);
        }
        var side = int.Parse(args[0], CultureInfo.InvariantCulture);
        var budget = long.Parse(args[1], CultureInfo.InvariantCulture) << 20;
        ArgumentOutOfRangeException.ThrowIfLessThan(side, 2);
        var directory = Path.Combine(Path.GetTempPath(), "echo-mesh-index-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var data = new BulkDataSession(budget, 0);
            using var work = new WorkBuffers(data, directory);
            var mesh = work.Surface();
            for (var y = 0; y <= side; y++)
            {
                for (var x = 0; x <= side; x++)
                {
                    mesh.Vertices.Add([x * .1, y * .1, Math.Sin(x * .1) * Math.Cos(y * .1) * .3]);
                }
            }
            for (var y = 0; y < side; y++)
            {
                for (var x = 0; x < side; x++)
                {
                    var a = y * (side + 1) + x;
                    mesh.Faces.Add([a, a + 1, a + side + 2]);
                    mesh.Faces.Add([a, a + side + 2, a + side + 1]);
                }
            }
            for (var iteration = 0; iteration < 6; iteration++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                var allocated = GC.GetTotalAllocatedBytes(true);
                var before = data.Statistics;
                var watch = Stopwatch.StartNew();
                using var index = new SurfaceTriangleIndex(mesh, default, work);
                var buildSeconds = watch.Elapsed.TotalSeconds;
                watch.Restart();
                var random = new Random(1771);
                var checksum = 0.0;
                for (var query = 0; query < 2000; query++)
                {
                    var point = new V3(random.NextDouble() * side * .1, random.NextDouble() * side * .1, 1);
                    var closest = index.Nearest(point, 3);
                    checksum += closest.DistanceSquared + closest.Position.X + closest.Position.Y + closest.Position.Z;
                }
                watch.Stop();
                using var process = Process.GetCurrentProcess();
                var stats = data.Statistics;
                var measurement = new MeshIndexMeasurement(iteration, mesh.Faces.Count, budget, buildSeconds, watch.Elapsed.TotalSeconds,
                    GC.GetTotalAllocatedBytes(false) - allocated, process.WorkingSet64, process.PeakWorkingSet64,
                    stats.PeakResidentBytes, stats.ReadBytes - before.ReadBytes, stats.WrittenBytes - before.WrittenBytes, checksum);
                Console.WriteLine(JsonSerializer.Serialize(measurement, FusionPlanningBenchmarkJsonContext.Default.MeshIndexMeasurement));
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
}
