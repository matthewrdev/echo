using System.ComponentModel.Composition;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Projects;

namespace Echo.Pipeline;

[Export(typeof(IArtifactStorageService)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class ArtifactStorageService : IArtifactStorageService
{
    private readonly IJsonCodec json;
    private readonly IFileOperations files;
    private readonly string[] stages;

    [ImportingConstructor]
    public ArtifactStorageService(IJsonCodec json, IFileOperations files, [ImportMany] IEnumerable<IPipelineStage> stages)
    {
        this.json = json;
        this.files = files;
        this.stages = stages.OrderBy(stage => stage.Order).Select(stage => stage.Name).ToArray();
    }

    public ArtifactStorageInventory Inspect(IProjectStore store)
    {
        using var lease = store.Lock("run");
        using var edit = store.Lock("edit");
        return Inventory(store);
    }

    public ArtifactDeletionPlan Plan(IProjectStore store, string? artifact)
    {
        using var lease = store.Lock("run");
        using var edit = store.Lock("edit");
        return MakePlan(store, Inventory(store), artifact);
    }

    public ArtifactDeletionPlan Delete(IProjectStore store, string? artifact, string confirmationToken)
    {
        using var lease = store.Lock("run");
        using var edit = store.Lock("edit");
        var inventory = Inventory(store);
        var plan = MakePlan(store, inventory, artifact);
        if (!string.Equals(plan.Token, confirmationToken, StringComparison.Ordinal))
        {
            throw new EchoException("Stored results changed since the deletion preview. Review a fresh preview before deleting.", 2);
        }

        // Invalidate references before removing bytes. An interrupted deletion can
        // leave unused files, but must never leave a saved run claiming missing output.
        if (!plan.PurgeAll)
        {
            foreach (var affected in plan.AffectedRuns)
            {
                var run = store.GetRun(affected.Key);
                var first = Array.IndexOf(stages, affected.Value);
                foreach (var key in run.Artifacts.Keys.ToArray())
                {
                    if (StageOrder(key) >= first)
                    {
                        run.Artifacts.Remove(key);
                    }
                }
                run.Status = "cancelled";
                run.Progress = null;
                run.Error = "Generated results removed from " + affected.Value + ". Process again to rebuild missing stages.";
                files.AtomicJson(store.PathFor($"runs/{run.Id}/run.json"), run);
            }
        }

        foreach (var path in plan.Paths)
        {
            Directory.Delete(store.PathFor(path), true);
        }
        if (plan.PurgeAll)
        {
            Directory.CreateDirectory(store.PathFor("artifacts"));
            Directory.CreateDirectory(store.PathFor("runs"));
        }
        return plan;
    }

    private ArtifactStorageInventory Inventory(IProjectStore store)
    {
        var project = store.Load();
        var result = new ArtifactStorageInventory();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AddHash(hash, project.Id + ":" + project.Revision);
        foreach (var folder in new[] { "runs", "artifacts", "work" })
        {
            var path = store.PathFor(folder);
            if (Directory.Exists(path))
            {
                result.Bytes += Scan(path, folder, hash);
            }
        }
        result.Token = Convert.ToHexString(hash.GetHashAndReset());
        var artifactRoot = store.PathFor("artifacts");
        if (Directory.Exists(artifactRoot))
        {
            foreach (var stageFolder in Directory.GetDirectories(artifactRoot).Order(StringComparer.Ordinal))
            {
                foreach (var directory in Directory.GetDirectories(stageFolder).Order(StringComparer.Ordinal))
                {
                    var item = new StoredArtifact
                    {
                        Path = Path.GetRelativePath(store.Root, directory),
                        Stage = Path.GetFileName(stageFolder)
                    };
                    foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                    {
                        item.Bytes += new FileInfo(file).Length;
                        item.FileCount++;
                    }
                    foreach (var name in new[] { "units.json", "recovery.json", "original-units.json" })
                    {
                        var index = Path.Combine(directory, name);
                        if (File.Exists(index))
                        {
                            using var document = JsonDocument.Parse(File.ReadAllBytes(index));
                            References(document.RootElement, item.Dependencies);
                        }
                    }
                    result.Artifacts.Add(item);
                }
            }
        }
        var byPath = result.Artifacts.ToDictionary(item => item.Path, StringComparer.Ordinal);
        foreach (var run in Runs(store))
        {
            result.RunCount++;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in run.Artifacts.Values)
            {
                MarkUsage(path, run.Id, byPath, visited);
            }
        }
        return result;
    }

    private long Scan(string path, string relative, IncrementalHash hash)
    {
        var info = new DirectoryInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new EchoException("Storage cleanup refuses symbolic links: " + relative, 2);
        }
        AddHash(hash, relative + ":" + info.LastWriteTimeUtc.Ticks);
        var bytes = 0L;
        foreach (var entry in info.GetFileSystemInfos().OrderBy(entry => entry.Name, StringComparer.Ordinal))
        {
            var child = relative + "/" + entry.Name;
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new EchoException("Storage cleanup refuses symbolic links: " + child, 2);
            }
            if (entry is DirectoryInfo)
            {
                bytes += Scan(entry.FullName, child, hash);
            }
            else if (entry is FileInfo file)
            {
                bytes += file.Length;
                AddHash(hash, child + ":" + file.Length + ":" + file.LastWriteTimeUtc.Ticks);
            }
        }
        return bytes;
    }

    private ArtifactDeletionPlan MakePlan(IProjectStore store, ArtifactStorageInventory inventory, string? artifact)
    {
        var plan = new ArtifactDeletionPlan { PurgeAll = artifact == null };
        if (plan.PurgeAll)
        {
            // Remove history first so a partial I/O failure cannot leave broken run references.
            foreach (var path in new[] { "runs", "artifacts", "work" })
            {
                if (Directory.Exists(store.PathFor(path)))
                {
                    plan.Paths.Add(path);
                }
            }
            plan.Bytes = inventory.Bytes;
            foreach (var run in Runs(store))
            {
                plan.AffectedRuns.Add(run.Id, stages[0]);
            }
        }
        else
        {
            if (!inventory.Artifacts.Any(item => item.Path == artifact))
            {
                throw new EchoException("Select an existing artifact directory from storage list.", 2);
            }
            var removed = new HashSet<string>(StringComparer.Ordinal) { artifact! };
            // Index artifacts cannot survive deletion of a referenced per-image unit.
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var item in inventory.Artifacts)
                {
                    if (item.Dependencies.Any(removed.Contains) && removed.Add(item.Path))
                    {
                        changed = true;
                    }
                }
            }
            foreach (var item in inventory.Artifacts)
            {
                if (removed.Contains(item.Path))
                {
                    plan.Paths.Add(item.Path);
                    plan.Bytes += item.Bytes;
                }
            }
            foreach (var run in Runs(store))
            {
                var first = stages.Length;
                foreach (var entry in run.Artifacts)
                {
                    if (removed.Contains(entry.Value))
                    {
                        first = Math.Min(first, StageOrder(entry.Key));
                    }
                }
                if (first < stages.Length)
                {
                    plan.AffectedRuns.Add(run.Id, stages[first]);
                }
            }
        }
        plan.Token = files.Hash(new
        {
            inventory.Token,
            artifact,
            plan.Paths,
            plan.AffectedRuns
        });
        return plan;
    }

    private int StageOrder(string key)
    {
        if (key == "semantics")
        {
            return Array.IndexOf(stages, "depth");
        }
        var stage = key.EndsWith("Preview", StringComparison.Ordinal) ? key[..^7] : key;
        var index = Array.IndexOf(stages, stage);
        return index < 0 ? stages.Length : index;
    }

    private IEnumerable<RunData> Runs(IProjectStore store)
    {
        var directory = store.PathFor("runs");
        if (!Directory.Exists(directory))
        {
            yield break;
        }
        foreach (var path in Directory.GetFiles(directory, "run.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var run = json.Read<RunData>(path);
            if (!Guid.TryParseExact(run.Id, "N", out _) || store.PathFor($"runs/{run.Id}/run.json") != path)
            {
                throw new EchoException("Invalid saved run path: " + path, 2);
            }
            yield return run;
        }
    }

    private void References(JsonElement value, List<string> paths)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                var path = value.GetString()!;
                if (path.StartsWith("artifacts/", StringComparison.Ordinal))
                {
                    paths.Add(path);
                }
                break;
            case JsonValueKind.Array:
                foreach (var child in value.EnumerateArray())
                {
                    References(child, paths);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    References(property.Value, paths);
                }
                break;
        }
    }

    private void MarkUsage(string path, string run, Dictionary<string, StoredArtifact> byPath, HashSet<string> visited)
    {
        if (!visited.Add(path) || !byPath.TryGetValue(path, out var artifact))
        {
            return;
        }
        artifact.Runs.Add(run);
        foreach (var dependency in artifact.Dependencies)
        {
            MarkUsage(dependency, run, byPath, visited);
        }
    }

    private void AddHash(IncrementalHash hash, string text)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(text + "\n"));
    }
}
