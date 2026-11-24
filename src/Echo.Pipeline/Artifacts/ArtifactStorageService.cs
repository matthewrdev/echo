using Echo.Storage.Journals;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Projects;
using Echo.Storage.Matching;
using Echo.Storage;

namespace Echo.Pipeline.Artifacts;

public sealed class ArtifactStorageService : IArtifactStorageService
{
    private readonly IJsonCodec json;
    private readonly IFileOperations files;
    private readonly IMatchingIndexStore matchingIndexes;
    private readonly string[] stages;

    public ArtifactStorageService(IJsonCodec json, IFileOperations files, IMatchingIndexStore matchingIndexes, IEnumerable<IPipelineStage> stages)
    {
        this.json = json;
        this.files = files;
        this.matchingIndexes = matchingIndexes;
        this.stages = stages.OrderBy(stage => stage.Order).Select(stage => stage.Name).ToArray();
    }

    public ArtifactStorageInventory Inspect(IProjectStore store)
    {
        using var lease = store.Lock("run");
        using var edit = store.Lock("edit");
        return Inventory(store);
    }

    public ArtifactDeletionPlan Plan(IProjectStore store, ArtifactDeletionRequest request)
    {
        using var lease = store.Lock("run");
        using var edit = store.Lock("edit");
        return MakePlan(store, Inventory(store), request);
    }

    public ArtifactDeletionPlan Delete(IProjectStore store, ArtifactDeletionRequest request, string confirmationToken)
    {
        using var lease = store.Lock("run");
        using var edit = store.Lock("edit");
        var inventory = Inventory(store);
        var plan = MakePlan(store, inventory, request);
        if (!string.Equals(plan.Token, confirmationToken, StringComparison.Ordinal))
        {
            throw new EchoException("Stored results changed since the deletion preview. Review a fresh preview before deleting.", 2);
        }

        // Invalidate references before removing bytes. An interrupted deletion can
        // leave unused files, but must never leave a saved run claiming missing output.
        if (plan.Kind == ArtifactDeletionKind.Artifact)
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
                store.SaveRun(run);
            }
        }

        foreach (var path in plan.Paths)
        {
            var fullPath = store.PathFor(path);
            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, true);
            }
            else
            {
                File.Delete(fullPath);
            }
        }

        if (plan.Kind == ArtifactDeletionKind.AllGenerated)
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

                    foreach (var name in new[] { "units.json", "recovery.json", "original-units.json", "processing.json" })
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
            foreach (var path in RunReferences(store, run))
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

    private ArtifactDeletionPlan MakePlan(IProjectStore store, ArtifactStorageInventory inventory, ArtifactDeletionRequest request)
    {
        var artifact = request.Artifact;
        var plan = new ArtifactDeletionPlan { Kind = request.Kind };
        if (!Enum.IsDefined(request.Kind) || (request.Kind == ArtifactDeletionKind.Artifact) != (artifact != null))
        {
            throw new EchoException("Choose one artifact, unused results, or all generated results.", 2);
        }

        if (request.RetainedRuns != null && request.Kind != ArtifactDeletionKind.Unused)
        {
            throw new EchoException("Saved run retention applies only to unused-result cleanup.", 2);
        }

        if (plan.Kind == ArtifactDeletionKind.Unused)
        {
            PlanUnused(store, inventory, request, plan);
        }
        else if (plan.Kind == ArtifactDeletionKind.AllGenerated)
        {
            plan.LogCutoff = DateTimeOffset.MaxValue;
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
            request.Kind,
            artifact,
            plan.Paths,
            plan.AffectedRuns,
            plan.RetainedRuns,
            plan.RemovedRuns
        });
        return plan;
    }

    private void PlanUnused(IProjectStore store, ArtifactStorageInventory inventory, ArtifactDeletionRequest request, ArtifactDeletionPlan plan)
    {
        var runs = Runs(store).OrderByDescending(run => run.Created).ToArray();
        var byId = runs.ToDictionary(run => run.Id, StringComparer.Ordinal);
        var retained = request.RetainedRuns?.ToHashSet(StringComparer.Ordinal) ?? byId.Keys.ToHashSet(StringComparer.Ordinal);
        if (retained.Any(id => !byId.ContainsKey(id)))
        {
            throw new EchoException("Saved runs changed. Refresh the cleanup preview.", 2);
        }

        var latest = runs.FirstOrDefault();
        plan.LogCutoff = latest?.Created ?? DateTimeOffset.MaxValue;
        if (latest != null)
        {
            retained.Add(latest.Id);
        }

        plan.RetainedRuns.AddRange(retained.Order(StringComparer.Ordinal));
        // Remove obsolete history first; an interrupted cleanup must not leave
        // a saved run pointing at an artifact that has already been deleted.
        foreach (var run in runs)
        {
            if (!retained.Contains(run.Id))
            {
                plan.RemovedRuns.Add(run.Id);
                AddCleanupPath(store, plan, $"runs/{run.Id}");
            }
            else if (run.Id != latest?.Id)
            {
                AddCleanupPath(store, plan, $"runs/{run.Id}/events.bin");
                AddCleanupPath(store, plan, $"runs/{run.Id}/traces");
            }
        }

        foreach (var artifact in inventory.Artifacts)
        {
            if (!artifact.Runs.Any(retained.Contains))
            {
                plan.Paths.Add(artifact.Path);
                plan.Bytes += artifact.Bytes;
            }
        }

        AddCleanupPath(store, plan, "work");
    }

    private void AddCleanupPath(IProjectStore store, ArtifactDeletionPlan plan, string path)
    {
        var fullPath = store.PathFor(path);
        if (Directory.Exists(fullPath))
        {
            plan.Bytes += Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);
        }
        else if (File.Exists(fullPath))
        {
            plan.Bytes += new FileInfo(fullPath).Length;
        }
        else
        {
            return;
        }

        plan.Paths.Add(path);
    }

    private IEnumerable<string> RunReferences(IProjectStore store, RunData run)
    {
        foreach (var path in run.Artifacts.Values)
        {
            yield return path;
        }

        foreach (var stage in new[] { "features", "depth" })
        {
            var folder = $"runs/{run.Id}/previews/{stage}";
            if (run.Artifacts.GetValueOrDefault(stage + "Preview") == folder)
            {
                foreach (var path in new ImagePreviewRecordCodec().ReadUnits(store.PathFor(folder + "/units.bin")).Values)
                {
                    yield return path;
                }
            }
        }

        var preview = $"runs/{run.Id}/previews/matching";
        var matchingJournal = store.PathFor(preview + "/pairs.bin");
        if (run.Artifacts.GetValueOrDefault("matchingPreview") == preview && File.Exists(matchingJournal))
        {
            foreach (var entry in matchingIndexes.Read(matchingJournal))
            {
                yield return entry.Artifact;
            }
        }
        // Completed child checkpoints can be resumable before their parent
        // stage has published an output, especially after cancellation.
        var metadata = store.PathFor($"runs/{run.Id}/processing.json");
        if (File.Exists(metadata))
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(metadata));
            if (document.RootElement.TryGetProperty("artifacts", out var artifacts))
            {
                foreach (var property in artifacts.EnumerateObject())
                {
                    yield return property.Name;
                }
            }
        }
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
            var run = store.GetRun(Path.GetFileName(Path.GetDirectoryName(path)!));
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
