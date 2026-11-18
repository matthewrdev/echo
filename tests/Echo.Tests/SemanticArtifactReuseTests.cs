using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Pipeline;
using Echo.Projects;

namespace Echo.Tests;

[Export]
public sealed class SemanticArtifactReuseTests
{
    private readonly IProjectStoreFactory stores;
    private readonly IRunContextFactory contexts;
    private readonly IFileOperations files;

    [ImportingConstructor]
    public SemanticArtifactReuseTests(IProjectStoreFactory stores, IRunContextFactory contexts, IFileOperations files)
    {
        this.stores = stores;
        this.contexts = contexts;
        this.files = files;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Photo classifications reuse saved work without loading a model and report reuse accurately", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "echo-semantic-reuse-" + Guid.NewGuid().ToString("N"));
            var store = stores.Open(root);
            store.Init();
            try
            {
                store.Edit(project =>
                {
                    project.Settings.DepthSuppressSky = true;
                    project.Images.Add(new Photo { Id = "one", Name = "one.jpg", Hash = "one", SourceHash = "original", Source = "one.jpg" });
                    project.Images.Add(new Photo { Id = "two", Name = "two.jpg", Hash = "two", SourceHash = "original", Source = "two.jpg" });
                });
                var classifier = new TestSceneClassifier();
                var artifacts = new SceneSemanticArtifacts(classifier, classifier, files, new OrderedWorkExecutor());
                Run(store, artifacts);
                Check(classifier.Classifications == 2 && classifier.ModelRequests == 1, "One session classifies both photos");
                classifier.AllowProcessing = false;
                var reused = Run(store, artifacts);
                Check(classifier.Classifications == 2 && classifier.ModelRequests == 1, "Second run skips model processing entirely");
                var log = File.ReadAllText(store.PathFor($"runs/{reused.Id}/events.ndjson"));
                Check(log.Contains("Reused saved sky") && !log.Contains("Detecting sky"), "Reuse must not masquerade as fresh inference");
                classifier.AllowProcessing = true;
                store.Edit(project => project.Images[0].SourceHash = "changed");
                Run(store, artifacts);
                Check(classifier.Classifications == 3, "Only changed photos need fresh inference");
                classifier.Implementation = "test-coreml-1";
                Run(store, artifacts);
                Check(classifier.Classifications == 5, "Changing the inference implementation must invalidate cached probabilities");
                classifier.Implementation = "test-cpu-1";
                classifier.AllowProcessing = false;
                Run(store, artifacts);
                Check(classifier.Classifications == 5, "Switching back to CPU reuses its compatible probability maps");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        });
    }

    private RunData Run(IProjectStore store, SceneSemanticArtifacts artifacts)
    {
        var run = new RunData { Id = Guid.NewGuid().ToString("N"), Target = "depth", Snapshot = store.Load() };
        using var context = contexts.Create(store, run, default, null);
        context.Start();
        artifacts.Build(context);
        context.Complete();
        return run;
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
