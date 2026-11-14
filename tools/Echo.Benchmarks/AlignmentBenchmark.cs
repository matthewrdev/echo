using System.ComponentModel.Composition;
using System.Diagnostics;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Optimization;
using Echo.Projects;
using Echo.Reconstruction;

namespace Echo.Benchmarks;

[Export]
public sealed class AlignmentBenchmark
{
    private readonly IProjectStoreFactory stores;
    private readonly IJsonCodec json;
    private readonly IFileOperations files;
    private readonly IReconstructor reconstructor;
    private readonly IBundleAdjuster bundle;

    [ImportingConstructor]
    public AlignmentBenchmark(IProjectStoreFactory stores, IJsonCodec json, IFileOperations files, IReconstructor reconstructor, IBundleAdjuster bundle)
    {
        this.stores = stores;
        this.json = json;
        this.files = files;
        this.reconstructor = reconstructor;
        this.bundle = bundle;
    }

    public int Run(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: Echo.Benchmarks align PROJECT RUN OUTPUT_DIRECTORY");
            return 2;
        }
        var store = stores.Open(args[0]);
        var run = store.GetRun(args[1]);
        var output = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(output);
        var pairs = new List<ImagePair>();
        foreach (var unit in json.Read<List<string>>(store.PathFor(run.Artifacts["matching"] + "/units.json")))
        {
            pairs.Add(json.Read<ImagePair>(store.PathFor(unit + "/pair.json")));
        }
        var clock = Stopwatch.StartNew();
        var scene = reconstructor.Align(run.Snapshot, pairs, default, preview =>
        {
            Console.WriteLine($"{clock.Elapsed.TotalSeconds:F1}s: {preview.Points.Count} point records; {preview.Bundle?.AcceptedIterations ?? 0} accepted iterations");
        });
        files.AtomicJson(Path.Combine(output, "initial.json"), scene);
        Console.WriteLine($"Initial: {scene.Bundle?.Tracks} tracks, error {scene.Bundle?.FinalMedianError:F4}");
        using var recovery = System.Text.Json.JsonDocument.Parse(File.ReadAllText(store.PathFor(run.Artifacts["alignment"] + "/recovery.json")));
        var originals = new List<ImagePair>(pairs);
        var index = 0;
        foreach (var unit in recovery.RootElement.GetProperty("recoveryUnits").EnumerateArray())
        {
            var recovered = json.Read<ImagePair>(store.PathFor(unit.GetString() + "/pair.json"));
            if (recovered.Matches.Count > 0)
            {
                foreach (var match in pairs[index].Matches)
                {
                    if (match.Manual)
                    {
                        recovered.Matches.Add(match);
                    }
                }
                pairs[index] = recovered;
            }
            index++;
        }
        if (index > 0)
        {
            pairs = AlignmentRecoveryFilter.Filter(run.Snapshot, scene, originals, pairs, default);
            bundle.Refine(run.Snapshot, scene, pairs, default, preview =>
            {
                Console.WriteLine($"{clock.Elapsed.TotalSeconds:F1}s: second pass, {preview.Bundle?.AcceptedIterations ?? 0} accepted iterations");
            });
        }
        files.AtomicJson(Path.Combine(output, "final.json"), scene);
        files.AtomicJson(Path.Combine(output, "metrics.json"), new
        {
            seconds = clock.Elapsed.TotalSeconds,
            scene.Bundle
        });
        return 0;
    }
}
