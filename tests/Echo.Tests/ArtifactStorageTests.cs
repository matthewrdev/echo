using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Pipeline;
using Echo.Projects;

namespace Echo.Tests;

[Export]
public sealed class ArtifactStorageTests
{
    private readonly IArtifactStorageService storage;
    private readonly IProjectStoreFactory stores;
    private readonly IFileOperations files;

    [ImportingConstructor]
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
            var plan = storage.Plan(store, unit);
            Check(plan.Paths.Count == 2 && plan.AffectedRuns.Count == 2, "Remove unit and dependent index, invalidate both runs");
            storage.Delete(store, unit, plan.Token);
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
            var plan = storage.Plan(store, unit);
            Check(plan.AffectedRuns[run.Id] == "depth", "Deleting semantic evidence invalidates Depth and downstream outputs");
            storage.Delete(store, unit, plan.Token);
            Check(store.GetRun(run.Id).Artifacts.Count == 1, "No dangling semantic or downstream run references");
        }));
        tests.Run("Purge is confirmed, rejects stale previews and active jobs, and preserves inputs", () => WithProject(store =>
        {
            SaveArtifact(store, "alignment", "test");
            var plan = storage.Plan(store, null);
            File.WriteAllText(store.PathFor("artifacts/new-file"), "changed");
            ExpectFailure(() => storage.Delete(store, null, plan.Token));
            plan = storage.Plan(store, null);
            using (store.Lock("run"))
            {
                ExpectFailure(() => storage.Delete(store, null, plan.Token));
            }
            File.WriteAllText(store.PathFor("sources/photo.jpg"), "source bytes");
            File.WriteAllText(store.PathFor("previews/photo.jpg"), "preview bytes");
            var originalProject = File.ReadAllText(store.PathFor("project.json"));
            storage.Delete(store, null, plan.Token);
            Check(!Directory.EnumerateFileSystemEntries(store.PathFor("artifacts")).Any(), "All artifacts removed");
            Check(File.ReadAllText(store.PathFor("sources/photo.jpg")) == "source bytes", "Source preserved");
            Check(File.ReadAllText(store.PathFor("project.json")) == originalProject, "Settings and hints unchanged");
            Check(File.Exists(store.PathFor("previews/photo.jpg")), "Photo thumbnails preserved");
        }));
        tests.Run("Storage deletion rejects path traversal and symbolic links", () => WithProject(store =>
        {
            ExpectFailure(() => storage.Plan(store, "../sources"));
            Directory.CreateSymbolicLink(store.PathFor("artifacts/linked"), store.PathFor("sources"));
            ExpectFailure(() => storage.Plan(store, null));
            Directory.Delete(store.PathFor("artifacts/linked"));
        }));
    }

    private string SaveArtifact(IProjectStore store, string stage, string name)
    {
        var path = $"artifacts/{stage}/{name}";
        Directory.CreateDirectory(store.PathFor(path));
        File.WriteAllText(store.PathFor(path + "/data"), "generated bytes");
        return path;
    }

    private RunData SaveRun(IProjectStore store, string alignment, string depth, string cloud)
    {
        var run = new RunData
        {
            Id = Guid.NewGuid().ToString("N"),
            Status = "completed",
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
