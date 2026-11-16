using Echo.Compute;
using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Pipeline;
using Echo.Projects;

namespace Echo.Tests;

[Export]
public sealed class ProcessingMetadataTests
{
    private readonly IProjectStoreFactory stores;
    private readonly IRunContextFactory contexts;
    private readonly IJsonCodec json;
    private readonly IFileOperations files;
    private readonly IComputeService compute;

    [ImportingConstructor]
    public ProcessingMetadataTests(IProjectStoreFactory stores, IRunContextFactory contexts, IJsonCodec json, IFileOperations files, IComputeService compute)
    {
        this.stores = stores;
        this.contexts = contexts;
        this.json = json;
        this.files = files;
        this.compute = compute;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Processing provenance is hashed with artifacts and preserved on reuse with a different requested backend", () => WithProject(store =>
        {
            var first = Context(store, "metal-validated");
            first.Start();
            var path = first.Build("depth-image", new
            {
                image = "one"
            }, SaveData, () => new ProcessingMetadata
            {
                Executions =
                [
                    new ProcessingExecution
                    {
                        RequestedBackend = "metal-validated", ActualBackend = "metal+cpu", Device = "Test GPU",
                        Precision = "float32 + float64 reference", Validation = "exact", OutputAuthority = "cpu-reference",
                        FallbackReason = "Corrected mismatches", AcceleratedSearches = 10, MatchedSearches = 7, CorrectedSearches = 3
                    }
                ]
            });
            first.Publish("depth", path);
            first.Complete();
            var metadataPath = store.PathFor(path + "/processing.json");
            var original = File.ReadAllText(metadataPath);
            var metadata = json.Read<ProcessingMetadata>(metadataPath);
            var manifest = json.Read<Artifact>(store.PathFor(path + "/manifest.json"));
            Check(metadata.AlgorithmVersion == "echo-0.1.0-pyramid7" && manifest.AlgorithmVersion == metadata.AlgorithmVersion, "Persist actual algorithm version");
            Check(metadata.Runtime.Length > 0 && metadata.OperatingSystem.Length > 0, "Persist execution environment");
            Check(manifest.Files["processing.json"] == files.HashFile(metadataPath), "Protect provenance with the checkpoint hash");
            var second = Context(store, "cpu");
            second.Start();
            var reused = second.Build("depth-image", new
            {
                image = "one"
            }, _ => throw new InvalidOperationException("Should reuse"),
                () => throw new InvalidOperationException("Should not replace existing provenance"));
            second.Complete();
            Check(path == reused && File.ReadAllText(metadataPath) == original, "Reuse cannot relabel or rewrite the producer");
            var runMetadata = json.Read<RunProcessingMetadata>(store.PathFor($"runs/{second.Run.Id}/processing.json"));
            Check(runMetadata.RequestedDepthBackend == "cpu", "Persist this run's requested backend separately");
            Check(runMetadata.Artifacts[path].Reused && runMetadata.Artifacts[path].MetadataPath == path + "/processing.json", "Run metadata points to the original producer");
            Check(store.GetRun(second.Run.Id).ProcessingMetadataFile == "processing.json", "Run manifest links the provenance index");
            File.AppendAllText(metadataPath, " ");
            try
            {
                second.Build("depth-image", new
                {
                    image = "one"
                }, SaveData);
                throw new InvalidOperationException("Tampering must invalidate the checkpoint");
            }
            catch (EchoException error) when (error.Message.StartsWith("Corrupt checkpoint", StringComparison.Ordinal))
            {
            }
        }));
        tests.Run("Packing-only Metal work is retained in nested checkpoint provenance", () => WithProject(store =>
        {
            using var context = Context(store, "cpu");
            context.Start();
            string? child = null;
            var parent = context.Build("texture", new
            {
                fixture = "packing-metadata"
            }, directory =>
            {
                child = context.Build("packing-fixture", new
                {
                    fixture = "integer"
                }, childDirectory =>
                {
                    compute.RecordExecution(new ComputeExecution
                    {
                        Operation = ComputeOperation.TexturePacking,
                        Backend = ComputeBackend.Metal,
                        Precision = ComputePrecision.IntegerExact,
                        Device = "Packing GPU",
                        Implementation = "packing-test-version"
                    });
                    SaveData(childDirectory);
                });
                SaveData(directory);
            });
            foreach (var path in new[] { parent, child! })
            {
                var metadata = json.Read<ProcessingMetadata>(store.PathFor(path + "/processing.json"));
                Check(metadata.Executions[0].ActualBackend == "cpu+metal", "Packing must prevent false CPU-only provenance");
                var packing = metadata.Executions.Single(execution => execution.Component.EndsWith(".texturepacking", StringComparison.Ordinal));
                Check(packing.Device == "Packing GPU" && packing.Implementation == "packing-test-version" && packing.Precision == "integer-exact", "Packing metadata was lost");
            }
            context.Complete();
        }));
        tests.Run("New CPU checkpoints always record provenance; older checkpoints remain explicitly unknown", () => WithProject(store =>
        {
            var context = Context(store, "cpu");
            context.Start();
            var path = context.Build("feature-image", new
            {
                image = "legacy"
            }, SaveData);
            var metadata = json.Read<ProcessingMetadata>(store.PathFor(path + "/processing.json"));
            Check(metadata.Executions.Single().ActualBackend == "cpu", "CPU processing is recorded too");
            var manifest = json.Read<Artifact>(store.PathFor(path + "/manifest.json"));
            manifest.Files.Remove("processing.json");
            File.Delete(store.PathFor(path + "/processing.json"));
            files.AtomicJson(store.PathFor(path + "/manifest.json"), manifest);
            context.Build("feature-image", new
            {
                image = "legacy"
            }, _ => throw new InvalidOperationException("Legacy checkpoint should reuse"));
            context.Complete();
            var saved = json.Read<RunProcessingMetadata>(store.PathFor($"runs/{context.Run.Id}/processing.json"));
            Check(saved.Artifacts[path].ProvenanceStatus == "unknown-legacy" && saved.Artifacts[path].MetadataPath == null, "Never invent provenance for legacy artifacts");
            Check(!File.Exists(store.PathFor(path + "/processing.json")), "Do not mutate immutable old checkpoints");
        }));
    }

    private RunContext Context(IProjectStore store, string backend)
    {
        var project = store.Load();
        project.Settings.DepthBackend = backend;
        return contexts.Create(store, new RunData { Id = Guid.NewGuid().ToString("N"), Snapshot = project }, default, null);
    }

    private void SaveData(string directory)
    {
        File.WriteAllText(Path.Combine(directory, "data"), "test");
    }

    private void WithProject(Action<IProjectStore> action)
    {
        var store = stores.Open(Path.Combine(Path.GetTempPath(), "echo-provenance-" + Guid.NewGuid().ToString("N")));
        store.Init();
        try
        {
            action(store);
        }
        finally
        {
            Directory.Delete(store.Root, true);
        }
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
