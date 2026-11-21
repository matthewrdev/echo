using Echo.Infrastructure;
using Echo.Migrate;
using Echo.Models;
using Echo.Projects;
using Echo.Storage.Matching;
using Echo.Storage;

namespace Echo.Tests;

public sealed class MatchingMigrationTests
{
    private readonly IProjectStoreFactory stores;
    private readonly IFileOperations files;
    private readonly IJsonCodec json;
    private readonly MatchingProjectMigrator migrator;
    private readonly IMatchingIndexStore indexes;

    public MatchingMigrationTests(IProjectStoreFactory stores, IFileOperations files, IJsonCodec json,
        MatchingProjectMigrator migrator, IMatchingIndexStore indexes)
    {
        this.stores = stores;
        this.files = files;
        this.json = json;
        this.migrator = migrator;
        this.indexes = indexes;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Project migration preserves saved artifacts and replaces preview history idempotently", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "echo-migrate-" + Guid.NewGuid().ToString("N"));
            var store = stores.Open(root);
            store.Init("Migration fixture");
            try
            {
                var feature = new FeatureSet { ImageId = "a", Features = [new Feature { X = 2, Y = 3, Descriptor = [.1f, .2f] }] };
                var pair = new ImagePair { A = "a", B = "b", Matches = [new Match { A = [2, 3], B = [4, 5], GeometricInlier = true }] };
                Legacy(store, "artifacts/feature-image/f", "features.json", feature);
                Legacy(store, "artifacts/match-pair/p", "pair.json", pair);
                Legacy(store, "artifacts/matching/m", "units.json", new List<string> { "artifacts/match-pair/p" });
                Legacy(store, "artifacts/matching-preview/old", "units.json", new List<string> { "artifacts/match-pair/p" });
                var run = new RunData { Id = Guid.NewGuid().ToString("N"), Status = "completed", StorageVersion = 3, Snapshot = store.Load() };
                run.Artifacts["matching"] = "artifacts/matching/m";
                run.Artifacts["matchingPreview"] = "artifacts/matching-preview/old";
                files.AtomicJson(store.PathFor($"runs/{run.Id}/run.json"), run);
                var originalFeatures = files.Hash(feature);
                var originalMatches = files.Hash(pair);
                migrator.Migrate(root);
                Check(originalFeatures == files.Hash(json.Read<FeatureSet>(store.PathFor("artifacts/feature-image/f/features.json"))), "Migration changed features");
                Check(originalMatches == files.Hash(json.Read<ImagePair>(store.PathFor("artifacts/match-pair/p/pair.json"))), "Migration changed matches");
                var changed = json.Read<RunData>(store.PathFor($"runs/{run.Id}/run.json"));
                Check(changed.Artifacts["matching"] == run.Artifacts["matching"], "Migration invalidated final artifact identity");
                Check(indexes.Read(store.PathFor(changed.Artifacts["matchingPreview"] + "/pairs.ndjson")).Count == 1, "Migrated partial results are not discoverable");
                Check(!Directory.Exists(store.PathFor("artifacts/matching-preview")), "Redundant snapshots remain");
                var hashes = Directory.GetFiles(root, "*.bin", SearchOption.AllDirectories).ToDictionary(path => path, files.HashFile);
                migrator.Migrate(root);
                Check(hashes.All(entry => files.HashFile(entry.Key) == entry.Value), "Repeated migration changed binary payloads");
                var pairDirectory = store.PathFor("artifacts/match-pair/p");
                var transaction = Path.Combine(pairDirectory, ".binary-migration");
                Directory.CreateDirectory(transaction);
                foreach (var path in Directory.GetFiles(pairDirectory))
                {
                    File.Copy(path, Path.Combine(transaction, Path.GetFileName(path)));
                }
                // Simulate interruption after installing a payload but before completing its header/manifest.
                foreach (var payload in Directory.GetFiles(transaction, "*.bin"))
                {
                    File.Delete(payload);
                }

                File.WriteAllText(Path.Combine(pairDirectory, "pair.json"), "{");
                migrator.Migrate(root);
                Check(originalMatches == files.Hash(json.Read<ImagePair>(Path.Combine(pairDirectory, "pair.json"))),
                    "Interrupted migration failed to recover its verified replacement");
                Check(!Directory.Exists(transaction), "Completed recovery retained its transaction");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        });
    }

    private void Legacy<T>(IProjectStore store, string artifact, string name, T value)
    {
        var path = store.PathFor(artifact + "/" + name);
        files.AtomicWrite(path, stream => json.Write(stream, value));
        files.AtomicJson(store.PathFor(artifact + "/manifest.json"), new Artifact
        {
            Stage = artifact.Split('/')[1],
            Key = artifact.Split('/')[2],
            Files = new Dictionary<string, string> { [name] = files.HashFile(path) }
        });
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
