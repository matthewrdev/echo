using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Projects;
using Echo.Storage;

namespace Echo.Migrate;

public sealed class MatchingProjectMigrator
{
    private readonly IProjectStoreFactory stores;
    private readonly IFileOperations files;
    private readonly IJsonCodec json;
    private readonly IMatchingIndexStore indexes;
    private readonly MatchingArtifactMigration artifacts;

    public MatchingProjectMigrator(IProjectStoreFactory stores, IFileOperations files, IJsonCodec json,
        IMatchingIndexStore indexes, MatchingArtifactMigration artifacts)
    {
        this.stores = stores;
        this.files = files;
        this.json = json;
        this.indexes = indexes;
        this.artifacts = artifacts;
    }

    public void Migrate(string root)
    {
        var store = stores.Open(root);
        using var runLock = store.Lock("run");
        using var editLock = store.Lock("edit");
        using var importLock = store.Lock("import");
        store.Load();
        Console.WriteLine("Migrating " + root);
        var converted = 0;
        foreach (var name in new[] { "features.json", "pair.json" })
        {
            var paths = Directory.EnumerateFiles(store.PathFor("artifacts"), name, SearchOption.AllDirectories)
                .Where(path => !path.Contains(".binary-migration", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
            var completed = 0;
            var parallel = new ParallelOptions
            {
                // Feature conversion retains full descriptor arrays for value verification.
                // Small independent pair transactions can overlap durable disk flushes.
                MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, name == "features.json" ? 2 : 8)
            };
            Parallel.ForEach(paths, parallel, path =>
            {
                if (artifacts.Convert(path, name == "features.json"))
                {
                    Interlocked.Increment(ref converted);
                }
                var count = Interlocked.Increment(ref completed);
                if (count % 100 == 0 || count == paths.Length)
                {
                    Console.WriteLine($"  {name}: {count:N0}/{paths.Length:N0} verified");
                }
            });
        }
        var matching = store.PathFor("artifacts/matching");
        if (Directory.Exists(matching))
        {
            foreach (var directory in Directory.GetDirectories(matching))
            {
                FinalIndex(store, directory);
            }
        }
        foreach (var path in Directory.EnumerateFiles(store.PathFor("runs"), "run.json", SearchOption.AllDirectories))
        {
            MigrateRun(store, path);
        }
        var previews = store.PathFor("artifacts/matching-preview");
        if (Directory.Exists(previews))
        {
            Directory.Delete(previews, true);
        }
        Console.WriteLine($"Completed {root}: {converted:N0} payloads converted and verified; preview history replaced with run journals.");
    }

    private void FinalIndex(IProjectStore store, string directory)
    {
        var target = Path.Combine(directory, "pairs.ndjson");
        var units = json.Read<List<string>>(Path.Combine(directory, "units.json"));
        using (var journal = indexes.Open(target))
        {
            for (var index = 0; index < units.Count; index++)
            {
                journal.Append(indexes.Describe(store.Root, units[index], index));
            }
            if (journal.Count != units.Count)
            {
                throw new InvalidDataException("Final matching index has unexpected entries.");
            }
        }
        var manifestPath = Path.Combine(directory, "manifest.json");
        var manifest = json.Read<Artifact>(manifestPath);
        manifest.Files["pairs.ndjson"] = files.HashFile(target);
        files.AtomicJson(manifestPath, manifest);
        artifacts.VerifyManifest(directory);
    }

    private void MigrateRun(IProjectStore store, string path)
    {
        var run = json.Read<RunData>(path);
        if (run.Artifacts.TryGetValue("matchingPreview", out var old) && old.StartsWith("artifacts/", StringComparison.Ordinal))
        {
            var units = json.Read<List<string>>(store.PathFor(old + "/units.json"));
            var preview = $"runs/{run.Id}/previews/matching";
            using (var journal = indexes.Open(store.PathFor(preview + "/pairs.ndjson")))
            {
                for (var index = 0; index < units.Count; index++)
                {
                    journal.Append(indexes.Describe(store.Root, units[index], index));
                }
            }
            run.Artifacts["matchingPreview"] = preview;
            files.AtomicJson(path, run);
        }
        var processingPath = store.PathFor($"runs/{run.Id}/processing.json");
        if (File.Exists(processingPath))
        {
            var processing = json.Read<RunProcessingMetadata>(processingPath);
            foreach (var key in processing.Artifacts.Keys.Where(key => key.StartsWith("artifacts/matching-preview/", StringComparison.Ordinal)).ToArray())
            {
                processing.Artifacts.Remove(key);
            }
            files.AtomicJson(processingPath, processing);
        }
    }
}
