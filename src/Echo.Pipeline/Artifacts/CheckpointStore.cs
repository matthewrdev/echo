using System.Reflection;
using System.Runtime.InteropServices;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Projects;
using Echo.Storage.Artifacts;
using Echo.Storage;

namespace Echo.Pipeline.Artifacts;

public sealed class CheckpointStore : ICheckpointStore
{
    private readonly IFileOperations fileOperations;
    private readonly IJsonCodec jsonCodec;
    private readonly IArtifactWriteObserver writes;
    private readonly CheckpointFingerprint fingerprint = new();
    private readonly string softwareVersion = typeof(CheckpointStore).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    public CheckpointStore(IFileOperations fileOperations, IJsonCodec jsonCodec, IArtifactWriteObserver writes)
    {
        this.fileOperations = fileOperations;
        this.jsonCodec = jsonCodec;
        this.writes = writes;
    }

    public string Build(IProjectStore store,
        string stage,
        object inputs,
        Action<string> action,
        CancellationToken cancellation,
        Action<string, Dictionary<string, object?>> progress,
        out bool recordedProcessing,
        Func<ProcessingMetadata>? processing = null,
        string? algorithmVersion = null,
        IReadOnlyList<object>? compatibleInputs = null)
    {
        cancellation.ThrowIfCancellationRequested();
        var version = algorithmVersion ?? stage switch
        {
            "alignment" => "echo-0.1.0-bundle7",
            "fusion" => "echo-0.1.0-tsdf6-minimum",
            "fusion-chunk" => "echo-fusion-batch2",
            "mesh" => "echo-0.1.0-surface10",
            "texture" => "echo-0.1.0-charts9-painted-priority",
            "export" => "echo-0.1.0-export4",
            "feature-image" => "echo-0.1.0-area2",
            "depth-image" => "echo-0.1.0-pyramid7",
            _ => "echo-0.1.0"
        };
        var key = fingerprint.Hash(stage, version, inputs);
        var relative = $"artifacts/{stage}/{key}";
        var final = store.PathFor(relative);
        var reused = TryReuse(store, stage, key, progress, out recordedProcessing);
        if (reused != null)
        {
            return reused;
        }
        // Only the owning algorithm can declare older inputs equivalent. Validate
        // those artifacts exactly like a current checkpoint, without rewriting them.
        foreach (var compatible in compatibleInputs ?? [])
        {
            cancellation.ThrowIfCancellationRequested();
            var compatibleKey = fingerprint.Hash(stage, version, compatible);
            reused = TryReuse(store, stage, compatibleKey, progress, out recordedProcessing);
            if (reused != null)
            {
                return reused;
            }
        }

        var temp = store.PathFor($"work/{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        progress("stageStarted", new Dictionary<string, object?>
        {
            ["stage"] = stage
        });
        try
        {
            using var written = writes.BeginScope();
            action(temp);
            cancellation.ThrowIfCancellationRequested();
            var metadata = processing?.Invoke() ?? new ProcessingMetadata
            {
                Executions = [new ProcessingExecution { Component = stage }]
            };
            metadata.Stage = stage;
            metadata.AlgorithmVersion = version;
            metadata.OperatingSystem = RuntimeInformation.OSDescription;
            metadata.Architecture = RuntimeInformation.ProcessArchitecture.ToString();
            metadata.Runtime = RuntimeInformation.FrameworkDescription;
            metadata.SoftwareVersion = softwareVersion;
            fileOperations.AtomicJson(Path.Combine(temp, "processing.json"), metadata);
            var files = Directory.GetFiles(temp, "*", SearchOption.AllDirectories).ToDictionary(p => Path.GetRelativePath(temp, p), p => written.TryGetChecksum(p, out var checksum) ? checksum : fileOperations.HashFile(p));
            fileOperations.AtomicJson(Path.Combine(temp, "manifest.json"), new Artifact
            {
                Stage = stage,
                AlgorithmVersion = version,
                Key = key,
                Files = files
            });
            Directory.CreateDirectory(Path.GetDirectoryName(final)!);
            Directory.Move(temp, final);
            recordedProcessing = true;
            progress("artifactCommitted", new Dictionary<string, object?>
            {
                ["stage"] = stage,
                ["artifact"] = relative
            });
            return relative;
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
    }

    private string? TryReuse(IProjectStore store, string stage, string key, Action<string, Dictionary<string, object?>> progress, out bool recordedProcessing)
    {
        var relative = $"artifacts/{stage}/{key}";
        var final = store.PathFor(relative);
        recordedProcessing = false;
        var manifestPath = Path.Combine(final, "manifest.json");
        if (File.Exists(manifestPath))
        {
            var manifest = jsonCodec.Read<Artifact>(manifestPath);
            if (manifest.Key != key || manifest.Files.Any(file => !File.Exists(Path.Combine(final, file.Key))
                || fileOperations.HashFile(Path.Combine(final, file.Key)) != file.Value))
            {
                throw new EchoException($"Corrupt checkpoint: {relative}. Preserve it for diagnosis and remove this artifact directory before retrying.");
            }

            recordedProcessing = manifest.Files.ContainsKey("processing.json");
            progress("artifactReused", new Dictionary<string, object?>
            {
                ["stage"] = stage,
                ["artifact"] = relative
            });
            return relative;
        }

        if (Directory.Exists(final))
        {
            throw new EchoException("Uncommitted artifact directory: " + final);
        }

        return null;
    }
}
