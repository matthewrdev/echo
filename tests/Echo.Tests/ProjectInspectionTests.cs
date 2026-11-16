using System.ComponentModel.Composition;
using System.Text.Json;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Pipeline;
using Echo.Projects;
using Echo.Storage;

namespace Echo.Tests;

[Export]
public sealed class ProjectInspectionTests
{
    private readonly IProjectStoreFactory stores;
    private readonly IProjectInspector inspector;
    private readonly IJsonCodec json;
    private readonly IBulkArtifactStore bulk;

    [ImportingConstructor]
    public ProjectInspectionTests(IProjectStoreFactory stores, IProjectInspector inspector, IJsonCodec json, IBulkArtifactStore bulk)
    {
        this.stores = stores;
        this.inspector = inspector;
        this.json = json;
        this.bulk = bulk;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Project summaries retain project and run data when saved alignment cannot be read", () => InProject(store =>
        {
            var scene = store.PathFor("artifacts/alignment/saved/scene.json");
            foreach (var contents in new string?[] { "{\"cameras\":[],\"points\":[]}", "{\"storage\":\"future-version\"}", "{", null })
            {
                File.Delete(scene);
                if (contents != null)
                {
                    File.WriteAllText(scene, contents);
                }
                var projectBefore = File.ReadAllBytes(store.PathFor("project.json"));
                var runBefore = File.ReadAllBytes(store.PathFor($"runs/{store.GetRun().Id}/run.json"));
                var result = inspector.Inspect(store, summary: true);
                Check(result.Project.Id == store.Load().Id && result.Run?.Id == store.GetRun().Id, "Project and run remain available");
                Check(result.Reconstruction == null && result.Cloud?.RetainedSamples == 17, "Only the unreadable artifact is omitted");
                Check(result.Warnings.Count == 1 && result.Warnings[0].Contains("alignment", StringComparison.Ordinal), "Artifact failure is visible");
                Check(projectBefore.SequenceEqual(File.ReadAllBytes(store.PathFor("project.json"))), "Project is not rewritten");
                Check(runBefore.SequenceEqual(File.ReadAllBytes(store.PathFor($"runs/{store.GetRun().Id}/run.json"))), "Saved run is not rewritten");
                Reject(() => inspector.Inspect(store));
            }
        }));
        tests.Run("Project summaries isolate cloud failures and preserve supported alignment", () => InProject(store =>
        {
            bulk.Write(store.PathFor("artifacts/alignment/saved/scene.json"), new ReconstructionScene
            {
                Cameras = [new CameraPose { ImageId = "saved-photo" }]
            });
            var healthy = inspector.Inspect(store, summary: true);
            Check(healthy.Warnings.Count == 0 && healthy.Reconstruction?.Cameras.Count == 1, "Supported artifacts remain readable");
            File.WriteAllText(store.PathFor("artifacts/cloud/saved/cloud.json"), "{");
            var result = inspector.Inspect(store, summary: true);
            Check(result.Reconstruction?.Cameras.Single().ImageId == "saved-photo" && result.Cloud == null, "Independent artifacts are preserved");
            Check(result.Warnings.Count == 1 && result.Warnings[0].Contains("cloud", StringComparison.Ordinal), "Cloud failure is visible");
            Reject(() => inspector.Inspect(store));
        }));
    }

    private void InProject(Action<IProjectStore> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "echo-project-inspection-" + Guid.NewGuid().ToString("N"));
        var store = stores.Open(root);
        store.Init("Saved project");
        try
        {
            var run = new RunData
            {
                Status = "completed",
                Snapshot = store.Load(),
                Artifacts = new Dictionary<string, string>
                {
                    ["alignment"] = "artifacts/alignment/saved",
                    ["cloud"] = "artifacts/cloud/saved"
                }
            };
            Directory.CreateDirectory(store.PathFor($"runs/{run.Id}"));
            Directory.CreateDirectory(store.PathFor(run.Artifacts["alignment"]));
            Directory.CreateDirectory(store.PathFor(run.Artifacts["cloud"]));
            File.WriteAllText(store.PathFor($"runs/{run.Id}/run.json"), json.Encode(run));
            File.WriteAllText(store.PathFor("artifacts/cloud/saved/cloud.json"), json.Encode(new DenseCloudReport { RetainedSamples = 17 }));
            action(store);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private void Reject(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException)
        {
            return;
        }
        throw new InvalidOperationException("Full artifact inspection must retain its validation errors.");
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
