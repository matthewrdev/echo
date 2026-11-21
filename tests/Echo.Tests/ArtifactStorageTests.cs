using Echo.Infrastructure;
using Echo.Models;
using Echo.Pipeline.Artifacts;
using Echo.Pipeline;
using Echo.Projects;

namespace Echo.Tests;

public sealed class ArtifactStorageTests
{
    private readonly IArtifactStorageService storage;
    private readonly IProjectStoreFactory stores;
    private readonly IFileOperations files;

    public ArtifactStorageTests(IArtifactStorageService storage, IProjectStoreFactory stores, IFileOperations files)
    {
        this.storage = storage;
        this.stores = stores;
        this.files = files;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Artifact deletion tracks shared image units and invalidates dependent runs", () => WithProject(store =>
        {
            var unit = SaveArtifact(store, "depth-image", "one");
            var depth = SaveArtifact(store, "depth", "index");
            files.AtomicJson(store.PathFor(depth + "/units.json"), new[] { unit });
            var alignment = SaveArtifact(store, "alignment", "kept");
            var cloud = SaveArtifact(store, "cloud", "output");
            var first = SaveRun(store, alignment, depth, cloud);
            var second = SaveRun(store, alignment, depth, cloud);
            var inventory = storage.Inspect(store);
            Check(inventory.Artifacts.Single(a => a.Path == unit).Runs.Count == 2, "Unit is shared by both runs");
            var plan = storage.Plan(store, Selection(unit));
            Check(plan.Paths.Count == 2 && plan.AffectedRuns.Count == 2, "Remove unit and dependent index, invalidate both runs");
            storage.Delete(store, Selection(unit), plan.Token);
            Check(!Directory.Exists(store.PathFor(unit)) && !Directory.Exists(store.PathFor(depth)), "Selected bytes and broken index must be deleted");
            foreach (var run in new[] { first, second })
            {
                var saved = store.GetRun(run.Id);
                Check(saved.Artifacts.Count == 1 && saved.Artifacts["alignment"] == alignment, "Earlier work survives, later references are removed");
                Check(saved.Status == "cancelled", "Run must no longer claim complete output");
            }
            Check(Directory.Exists(store.PathFor(cloud)), "Other downstream bytes remain available to storage browser");
        }));
        tests.Run("Semantic mask cleanup invalidates reconstruction and retains original depth provenance", () => WithProject(store =>
        {
            var unit = SaveArtifact(store, "semantic-image", "plant-mask");
            var semantics = SaveArtifact(store, "semantics", "index");
            files.AtomicJson(store.PathFor(semantics + "/units.json"), new Dictionary<string, string> { ["photo"] = unit });
            var original = SaveArtifact(store, "depth-image", "original");
            var depth = SaveArtifact(store, "depth", "recovered");
            files.AtomicJson(store.PathFor(depth + "/original-units.json"), new[] { original });
            var alignment = SaveArtifact(store, "alignment", "kept");
            var cloud = SaveArtifact(store, "cloud", "output");
            var run = SaveRun(store, alignment, depth, cloud);
            run.Artifacts["semantics"] = semantics;
            files.AtomicJson(store.PathFor($"runs/{run.Id}/run.json"), run);
            var inventory = storage.Inspect(store);
            Check(inventory.Artifacts.Single(item => item.Path == original).Runs.Contains(run.Id), "Original maps are part of recovery provenance");
            var plan = storage.Plan(store, Selection(unit));
            Check(plan.AffectedRuns[run.Id] == "depth", "Deleting semantic evidence invalidates Depth and downstream outputs");
            storage.Delete(store, Selection(unit), plan.Token);
            Check(store.GetRun(run.Id).Artifacts.Count == 1, "No dangling semantic or downstream run references");
        }));
        tests.Run("Purge is confirmed, rejects stale previews and active jobs, and preserves inputs", () => WithProject(store =>
        {
            SaveArtifact(store, "alignment", "test");
            var plan = storage.Plan(store, Selection(null));
            File.WriteAllText(store.PathFor("artifacts/new-file"), "changed");
            ExpectFailure(() => storage.Delete(store, Selection(null), plan.Token));
            plan = storage.Plan(store, Selection(null));
            using (store.Lock("run"))
            {
                ExpectFailure(() => storage.Delete(store, Selection(null), plan.Token));
            }
            File.WriteAllText(store.PathFor("sources/photo.jpg"), "source bytes");
            File.WriteAllText(store.PathFor("previews/photo.jpg"), "preview bytes");
            var originalProject = File.ReadAllText(store.PathFor("project.json"));
            storage.Delete(store, Selection(null), plan.Token);
            Check(!Directory.EnumerateFileSystemEntries(store.PathFor("artifacts")).Any(), "All artifacts removed");
            Check(File.ReadAllText(store.PathFor("sources/photo.jpg")) == "source bytes", "Source preserved");
            Check(File.ReadAllText(store.PathFor("project.json")) == originalProject, "Settings and hints unchanged");
            Check(File.Exists(store.PathFor("previews/photo.jpg")), "Photo thumbnails preserved");
        }));
        tests.Run("Storage deletion rejects path traversal and symbolic links", () => WithProject(store =>
        {
            ExpectFailure(() => storage.Plan(store, Selection("../sources")));
            Directory.CreateSymbolicLink(store.PathFor("artifacts/linked"), store.PathFor("sources"));
            ExpectFailure(() => storage.Plan(store, Selection(null)));
            Directory.Delete(store.PathFor("artifacts/linked"));
        }));
        tests.Run("Unused cleanup retains latest, display fallbacks, dependencies and resumable child checkpoints", () => WithProject(store =>
        {
            var alignment = SaveArtifact(store, "alignment", "shared");
            var unit = SaveArtifact(store, "depth-image", "shared");
            var depth = SaveArtifact(store, "depth", "shared");
            files.AtomicJson(store.PathFor(depth + "/units.json"), new[] { unit });
            var source = SaveArtifact(store, "fusion", "source");
            var cloud = SaveArtifact(store, "cloud", "fallback");
            files.AtomicJson(store.PathFor(cloud + "/processing.json"), new
            {
                sourceArtifacts = new[] { source }
            });
            var support = SaveRun(store, alignment, depth, cloud, 2);
            var obsolete = SaveArtifact(store, "cloud", "obsolete");
            var old = SaveRun(store, alignment, depth, obsolete, 3);
            var latest = SaveRun(store, alignment, depth, cloud);
            latest.Artifacts.Remove("cloud");
            files.AtomicJson(store.PathFor($"runs/{latest.Id}/run.json"), latest);
            var partial = SaveArtifact(store, "fusion-chunk", "resumable");
            files.AtomicJson(store.PathFor($"runs/{latest.Id}/processing.json"), new
            {
                artifacts = new Dictionary<string, object> { [partial] = new { status = "completed" } }
            });
            var orphan = SaveArtifact(store, "depth-image", "orphan");
            foreach (var run in new[] { support, old, latest })
            {
                File.WriteAllText(store.PathFor($"runs/{run.Id}/events.ndjson"), "history\n");
                files.AtomicJson(store.PathFor($"runs/{run.Id}/traces/index.json"), new
                {
                    saved = true
                });
            }
            files.AtomicJson(store.PathFor("work/abandoned/scratch.json"), new
            {
                temporary = true
            });
            File.WriteAllText(store.PathFor("sources/photo.jpg"), "source");
            var project = File.ReadAllText(store.PathFor("project.json"));
            var request = new ArtifactDeletionRequest { Kind = ArtifactDeletionKind.Unused, RetainedRuns = [support.Id] };
            var plan = storage.Plan(store, request);
            Check(plan.RetainedRuns.ToHashSet().SetEquals([latest.Id, support.Id]), "Latest must be retained even when omitted by the client");
            Check(plan.RemovedRuns.SequenceEqual([old.Id]), "Only obsolete run history should be removed");
            Check(plan.LogCutoff == latest.Created, "Desktop log cutoff must follow the latest retained attempt");
            storage.Delete(store, request, plan.Token);
            foreach (var path in new[] { alignment, depth, unit, source, cloud, partial })
            {
                Check(Directory.Exists(store.PathFor(path)), "Required artifact was deleted: " + path);
            }
            foreach (var path in new[] { obsolete, orphan, "work", $"runs/{old.Id}", $"runs/{support.Id}/traces" })
            {
                Check(!Directory.Exists(store.PathFor(path)), "Unused data survived cleanup: " + path);
            }
            Check(!File.Exists(store.PathFor($"runs/{support.Id}/events.ndjson")), "Older supporting logs should be cleared");
            Check(File.Exists(store.PathFor($"runs/{latest.Id}/events.ndjson")), "Latest logs must survive");
            Check(File.Exists(store.PathFor($"runs/{latest.Id}/traces/index.json")), "Latest trace must survive");
            Check(store.GetRun(support.Id).Artifacts["cloud"] == cloud, "Display fallback run changed");
            Check(store.GetRun(latest.Id).Artifacts.Count == 2, "Cleanup rewrote latest processing state");
            Check(File.ReadAllText(store.PathFor("project.json")) == project && File.Exists(store.PathFor("sources/photo.jpg")), "Inputs changed");
            Check(storage.Plan(store, request).Paths.Count == 0, "Cleanup should be idempotent");
        }));
        tests.Run("Unused cleanup rejects stale plans, unknown retention and active jobs", () => WithProject(store =>
        {
            var shared = SaveArtifact(store, "alignment", "shared");
            var old = SaveRun(store, shared, shared, shared, 1);
            var request = new ArtifactDeletionRequest { Kind = ArtifactDeletionKind.Unused, RetainedRuns = [] };
            var plan = storage.Plan(store, request);
            SaveRun(store, shared, shared, shared);
            ExpectFailure(() => storage.Delete(store, request, plan.Token));
            Check(Directory.Exists(store.PathFor($"runs/{old.Id}")), "Stale preview removed history");
            ExpectFailure(() => storage.Plan(store, new ArtifactDeletionRequest { Kind = ArtifactDeletionKind.Unused, RetainedRuns = ["missing"] }));
            ExpectFailure(() => storage.Plan(store, new ArtifactDeletionRequest { Kind = ArtifactDeletionKind.AllGenerated, RetainedRuns = [] }));
            ExpectFailure(() => storage.Plan(store, new ArtifactDeletionRequest { Kind = ArtifactDeletionKind.Artifact }));
            plan = storage.Plan(store, request);
            using (store.Lock("run"))
            {
                ExpectFailure(() => storage.Delete(store, request, plan.Token));
            }
            var keepHistory = storage.Plan(store, new ArtifactDeletionRequest { Kind = ArtifactDeletionKind.Unused });
            Check(keepHistory.RemovedRuns.Count == 0 && keepHistory.RetainedRuns.Count == 2, "Unspecified retention must keep saved runs");
        }));
        tests.Run("Unused cleanup handles orphan files without saved history", () => WithProject(store =>
        {
            var artifact = SaveArtifact(store, "mesh", "orphan");
            var request = new ArtifactDeletionRequest { Kind = ArtifactDeletionKind.Unused, RetainedRuns = [] };
            var plan = storage.Plan(store, request);
            Check(plan.Paths.SequenceEqual([artifact]) && plan.LogCutoff == DateTimeOffset.MaxValue, "Orphan cleanup must also clear old command history");
            storage.Delete(store, request, plan.Token);
            Check(!Directory.Exists(store.PathFor(artifact)), "Orphan artifact survived");
        }));
    }

    private ArtifactDeletionRequest Selection(string? artifact)
    {
        return new ArtifactDeletionRequest
        {
            Kind = artifact == null ? ArtifactDeletionKind.AllGenerated : ArtifactDeletionKind.Artifact,
            Artifact = artifact
        };
    }

    private string SaveArtifact(IProjectStore store, string stage, string name)
    {
        var path = $"artifacts/{stage}/{name}";
        Directory.CreateDirectory(store.PathFor(path));
        File.WriteAllText(store.PathFor(path + "/data"), "generated bytes");
        return path;
    }

    private RunData SaveRun(IProjectStore store, string alignment, string depth, string cloud, int ageDays = 0)
    {
        var run = new RunData
        {
            Id = Guid.NewGuid().ToString("N"),
            Status = "completed",
            Created = DateTimeOffset.UtcNow.AddDays(-ageDays),
            Snapshot = store.Load(),
            Artifacts = new Dictionary<string, string> { ["alignment"] = alignment, ["depth"] = depth, ["cloud"] = cloud }
        };
        files.AtomicJson(store.PathFor($"runs/{run.Id}/run.json"), run);
        return run;
    }

    private void WithProject(Action<IProjectStore> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "echo-storage-" + Guid.NewGuid().ToString("N"));
        var store = stores.Open(root);
        store.Init("Storage tests");
        try
        {
            action(store);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private void ExpectFailure(Action action)
    {
        try
        {
            action();
        }
        catch (EchoException)
        {
            return;
        }
        throw new InvalidOperationException("Expected safe refusal");
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
