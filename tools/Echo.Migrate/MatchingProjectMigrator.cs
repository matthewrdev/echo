using Echo.Infrastructure;
using Echo.Models;
using Echo.Projects;
using Echo.Storage.Matching;
using Echo.Storage;

namespace Echo.Migrate;

public sealed class MatchingProjectMigrator
{
    private readonly IProjectStoreFactory stores;
    private readonly IFileOperations files;
    private readonly IJsonCodec json;
    private readonly IMatchingIndexStore indexes;
    private readonly MatchingArtifactMigration artifacts;
    private readonly PackedStorageMigrator packed;

    public MatchingProjectMigrator(IProjectStoreFactory stores, IFileOperations files, IJsonCodec json,
        IMatchingIndexStore indexes, MatchingArtifactMigration artifacts, PackedStorageMigrator packed)
    {
        this.stores = stores;
        this.files = files;
        this.json = json;
        this.indexes = indexes;
        this.artifacts = artifacts;
        this.packed = packed;
    }

    public void MigrateAlignmentPreviews(string root)
    {
        var store = stores.Open(root);
        using var runLock = store.Lock("run");
        var count = packed.MigrateAlignmentPreviews(store);
        Console.WriteLine($"Completed {root}: {count:N0} alignment previews packed.");
    }

    public void MigrateRunProvenance(string root)
    {
        var store = stores.Open(root);
        using var runLock = store.Lock("run");
        var count = packed.MigrateRunProvenance(store);
        Console.WriteLine($"Completed {root}: {count:N0} run provenance journals packed.");
    }

    public void Migrate(string root)
    {
        var store = stores.Open(root);
        using var runLock = store.Lock("run");
        using var importLock = store.Lock("import");
        using var editLock = store.Lock("edit");
        packed.MigrateProject(store);
        store.Load();
        Console.WriteLine("Migrating " + root);
        var converted = 0;
        var previewCount = packed.MigrateAlignmentPreviews(store);
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

        packed.MigrateCloudArtifacts(store);
        packed.MigrateFaceFlags(store);
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

        packed.MigrateRunProvenance(store);

        var previews = store.PathFor("artifacts/matching-preview");
        if (Directory.Exists(previews))
        {
            Directory.Delete(previews, true);
        }

        Console.WriteLine($"Completed {root}: {converted:N0} legacy matching payloads converted and verified; {previewCount:N0} alignment previews packed.");
    }

    private void FinalIndex(IProjectStore store, string directory)
    {
        var target = Path.Combine(directory, "pairs.bin");
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
        manifest.Files.Remove("pairs.ndjson");
        manifest.Files["pairs.bin"] = files.HashFile(target);
        files.AtomicJson(manifestPath, manifest);
        artifacts.VerifyManifest(directory);
        File.Delete(Path.Combine(directory, "pairs.ndjson"));
    }

    private void MigrateRun(IProjectStore store, string path)
    {
        var run = packed.ReadRun(store, path, out var legacyRun);
        var changed = packed.MigrateRun(store, run) || legacyRun;
        if (run.Artifacts.TryGetValue("matchingPreview", out var old))
        {
            var preview = $"runs/{run.Id}/previews/matching";
            if (old.StartsWith("artifacts/", StringComparison.Ordinal))
            {
                var units = json.Read<List<string>>(store.PathFor(old + "/units.json"));
                using var journal = indexes.Open(store.PathFor(preview + "/pairs.bin"));
                for (var index = 0; index < units.Count; index++)
                {
                    journal.Append(indexes.Describe(store.Root, units[index], index));
                }

                run.Artifacts["matchingPreview"] = preview;
                changed = true;
            }
            else
            {
                var legacy = store.PathFor(old + "/pairs.ndjson");
                if (File.Exists(legacy))
                {
                    using (var journal = indexes.Open(store.PathFor(preview + "/pairs.bin")))
                    {
                        foreach (var line in File.ReadLines(legacy))
                        {
                            var entry = System.Text.Json.JsonSerializer.Deserialize(line, CoreJsonReadContext.Default.MatchingIndexEntry)
                                ?? throw new InvalidDataException("Invalid legacy matching index entry.");
                            journal.Append(entry);
                        }
                    }

                    File.Delete(legacy);
                }
            }
        }

        if (changed)
        {
            var updated = File.GetLastWriteTimeUtc(path);
            store.SaveRun(run);
            File.SetLastWriteTimeUtc(path, updated);
        }

        var processingPath = store.PathFor($"runs/{run.Id}/processing.json");
        if (File.Exists(processingPath))
        {
            var processing = json.Read<RunProcessingMetadata>(processingPath);
            var obsolete = processing.Artifacts.Keys.Where(key => key.StartsWith("artifacts/matching-preview/", StringComparison.Ordinal)).ToArray();
            foreach (var key in obsolete)
            {
                processing.Artifacts.Remove(key);
            }

            if (obsolete.Length > 0)
            {
                files.AtomicJson(processingPath, processing);
            }
        }
    }
}
