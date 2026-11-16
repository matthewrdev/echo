using System.ComponentModel.Composition;
using System.Reflection;
using System.Runtime.InteropServices;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Projects;

namespace Echo.Pipeline;

[Export(typeof(ICheckpointStore)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class CheckpointStore : ICheckpointStore
{
    private readonly IFileOperations fileOperations;
    private readonly IJsonCodec jsonCodec;

    [ImportingConstructor]
    public CheckpointStore(IFileOperations fileOperations, IJsonCodec jsonCodec)
    {
        this.fileOperations = fileOperations;
        this.jsonCodec = jsonCodec;
    }

    public string Build(IProjectStore store,
        string stage,
        object inputs,
        Action<string> action,
        CancellationToken cancellation,
        Action<string, object> progress,
        Func<ProcessingMetadata>? processing = null,
        string? algorithmVersion = null)
    {
        cancellation.ThrowIfCancellationRequested();
        var version = algorithmVersion ?? stage switch
        {
            "alignment" => "echo-0.1.0-bundle5",
            "fusion" => "echo-0.1.0-tsdf5-resident",
            "fusion-chunk" => "echo-fusion-batch2",
            "mesh" => "echo-0.1.0-surface9",
            "texture" => "echo-0.1.0-charts8-bounded-gap",
            "export" => "echo-0.1.0-export4",
            "feature-image" => "echo-0.1.0-area2",
            "depth-image" => "echo-0.1.0-pyramid7",
            _ => "echo-0.1.0"
        };
        var key = fileOperations.Hash(new
        {
            storageVersion = 3,
            version,
            stage,
            inputs
        });
        var relative = $"artifacts/{stage}/{key}";
        var final = store.PathFor(relative);
        var manifestPath = Path.Combine(final, "manifest.json");
        if (File.Exists(manifestPath))
        {
            var manifest = jsonCodec.Read<Artifact>(manifestPath);
            if (manifest.Key != key || manifest.Files.Any(f => !File.Exists(Path.Combine(final, f.Key)) || fileOperations.HashFile(Path.Combine(final, f.Key)) != f.Value))
            {
                throw new EchoException($"Corrupt checkpoint: {relative}. Preserve it for diagnosis and remove this artifact directory before retrying.");
            }

            progress("artifactReused", new
            {
                stage,
                artifact = relative
            });
            return relative;
        }

        if (Directory.Exists(final))
        {
            throw new EchoException("Uncommitted artifact directory: " + final);
        }

        var temp = store.PathFor($"work/{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        progress("stageStarted", new
        {
            stage
        });
        try
        {
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
            metadata.SoftwareVersion = typeof(CheckpointStore).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
            fileOperations.AtomicJson(Path.Combine(temp, "processing.json"), metadata);
            var files = Directory.GetFiles(temp, "*", SearchOption.AllDirectories).ToDictionary(p => Path.GetRelativePath(temp, p), fileOperations.HashFile);
            fileOperations.AtomicJson(Path.Combine(temp, "manifest.json"), new Artifact
            {
                Stage = stage,
                AlgorithmVersion = version,
                Key = key,
                Files = files
            });
            Directory.CreateDirectory(Path.GetDirectoryName(final)!);
            Directory.Move(temp, final);
            progress("artifactCommitted", new
            {
                stage,
                artifact = relative
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
}
