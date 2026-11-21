using System.Diagnostics;
using Echo.Infrastructure;
using Echo.Meshing;
using Echo.Models;
using Echo.Simplification;
using Echo.Storage.Artifacts;
using Echo.Storage.Resources;
using Echo.Storage;

namespace Echo.Benchmarks;

public sealed class SimplificationBenchmark
{
    private readonly IEdgeCollapse simplifier;
    private readonly IBulkArtifactStore artifacts;
    private readonly IFileOperations files;
    private readonly IJsonCodec json;
    private readonly IMeshBuilder meshes;

    public SimplificationBenchmark(IEdgeCollapse simplifier, IBulkArtifactStore artifacts, IFileOperations files, IJsonCodec json, IMeshBuilder meshes)
    {
        this.simplifier = simplifier;
        this.artifacts = artifacts;
        this.files = files;
        this.json = json;
        this.meshes = meshes;
    }

    public int Run(string[] args)
    {
        if (args.Length is < 1 or > 3)
        {
            Console.Error.WriteLine("Usage: Echo.Benchmarks simplification OUTPUT_DIRECTORY [GRID_SIZE] [CACHE_MB]");
            return 2;
        }
        var size = args.Length > 1 ? int.Parse(args[1]) : 384;
        var cacheMb = args.Length > 2 ? int.Parse(args[2]) : 32;
        if (size is < 2 or > 4096 || cacheMb < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(args));
        }

        var directory = Path.Combine(Path.GetFullPath(args[0]), "simplification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var session = artifacts.OpenSession(cacheMb * 1048576L, 0);
            using var work = new WorkBuffers(session, directory);
            var surface = CreateSurface(size, work);
            Measure(surface, surface.Faces.Count / 10, .001, work, session, cacheMb);
            return 0;
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    public int RunArtifact(string[] args)
    {
        if (args.Length is < 2 or > 4)
        {
            Console.Error.WriteLine("Usage: Echo.Benchmarks simplification-artifact VOLUME_OR_SURFACE_JSON OUTPUT_DIRECTORY [CACHE_MB] [TARGET_FACES]");
            return 2;
        }
        var output = Path.GetFullPath(args[1]);
        var cacheMb = args.Length > 2 ? int.Parse(args[2]) : 16384;
        var target = args.Length > 3 ? int.Parse(args[3]) : 1500000;
        var directory = Path.Combine(output, "scratch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var session = artifacts.OpenSession(cacheMb * 1048576L, 0);
            using var work = new WorkBuffers(session, directory);
            using var header = System.Text.Json.JsonDocument.Parse(File.ReadAllText(args[0]));
            var tolerance = .001;
            Surface source;
            if (header.RootElement.GetProperty("kind").GetString() == "volume")
            {
                var volume = session.Read<FusedVolume>(args[0]);
                tolerance = volume.VoxelSize * .15;
                var last = "";
                source = meshes.Extract(volume, default, work, operationProgress: update =>
                {
                    if (update.Operation != last)
                    {
                        Console.Error.WriteLine(update.Operation);
                        last = update.Operation;
                    }
                });
                files.AtomicData(Path.Combine(output, "source.json"), source);
                files.AtomicJson(Path.Combine(output, "tolerance.json"), tolerance);
            }
            else
            {
                source = session.Read<Surface>(args[0]);
                var savedTolerance = Path.Combine(Path.GetDirectoryName(args[0])!, "tolerance.json");
                if (File.Exists(savedTolerance))
                {
                    tolerance = System.Text.Json.JsonSerializer.Deserialize<double>(File.ReadAllText(savedTolerance));
                }
            }
            Console.Error.WriteLine($"Input: {source.Vertices.Count:N0} vertices, {source.Faces.Count:N0} triangles; tolerance {tolerance:G17}.");
            var reduced = Measure(source, target, tolerance, work, session, cacheMb);
            files.AtomicData(Path.Combine(output, "reduced.json"), reduced);
            return 0;
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private Surface Measure(Surface surface, int target, double tolerance, WorkBuffers work, BulkDataSession session, int cacheMb)
    {
        var before = session.Statistics;
        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var clock = Stopwatch.StartNew();
        var collapseStarted = TimeSpan.Zero;
        var collapseSeconds = 0.0;
        var operation = "";
        var reduced = simplifier.Reduce(surface, target, default, tolerance, work: work, progress: update =>
        {
            if (update.Operation != operation)
            {
                if (operation is "Collapse edges" or "Parallel edge collapse")
                {
                    collapseSeconds += (clock.Elapsed - collapseStarted).TotalSeconds;
                }
                if (update.Operation is "Collapse edges" or "Parallel edge collapse")
                {
                    collapseStarted = clock.Elapsed;
                }
                operation = update.Operation;
                Console.Error.WriteLine($"{clock.Elapsed.TotalSeconds:F2}s: {update.Message}");
            }
        });
        clock.Stop();
        var cpuSeconds = (process.TotalProcessorTime - cpuBefore).TotalSeconds;
        var after = session.Statistics;
        Console.WriteLine(json.Encode(new SimplificationMeasurement(surface.Faces.Count, reduced.Faces.Count, cacheMb,
            clock.Elapsed.TotalSeconds, collapseSeconds, after.ReadBytes - before.ReadBytes, after.WrittenBytes - before.WrittenBytes,
            after.Evictions - before.Evictions, after.Hits + after.Misses - before.Hits - before.Misses,
            after.PeakResidentBytes, files.Hash(reduced), cpuSeconds)));
        return reduced;
    }

    private Surface CreateSurface(int size, WorkBuffers work)
    {
        var surface = work.Surface();
        for (var y = 0; y <= size; y++)
        {
            for (var x = 0; x <= size; x++)
            {
                surface.Vertices.Add([x * .02, y * .02, .1 * Math.Sin(x * .05) * Math.Cos(y * .05)]);
                if (x > 0 && y > 0)
                {
                    var vertex = y * (size + 1) + x;
                    surface.Faces.Add([vertex - size - 2, vertex - 1, vertex]);
                    surface.Faces.Add([vertex - size - 2, vertex, vertex - size - 1]);
                }
            }
        }
        return surface;
    }
}
