using Echo.Cli;
using Echo.Projects;

namespace Echo.Tests;

public sealed class ResetCommandTests
{
    private readonly ICommandApplication application;
    private readonly IProjectStoreFactory storeFactory;

    public ResetCommandTests(ICommandApplication application, IProjectStoreFactory storeFactory)
    {
        this.application = application;
        this.storeFactory = storeFactory;
    }

    internal void Run(TestSuite tests)
    {
        var root = Path.Combine(Path.GetTempPath(), "echo-reset-command-" + Guid.NewGuid().ToString("N"));
        var store = storeFactory.Open(root);
        store.Init("Reset command regression");
        var earlierStages = new[] { "features", "matching", "alignment" };
        var affectedStages = new[] { "depth", "cloud", "fusion", "mesh", "texture", "export" };
        store.Edit(project =>
        {
            foreach (var stage in earlierStages.Concat(affectedStages))
            {
                project.StageRevisions[stage] = "original";
            }
        });

        try
        {
            tests.Run("Desktop reset command accepts --from and invalidates the selected stage and descendants", () =>
            {
                var exitCode = application.Run(["--project", root, "pipeline", "reset", "--from", "depth"]);
                Check(exitCode == 0, "The exact desktop command must succeed");
                var revisions = store.Load().StageRevisions;
                Check(earlierStages.All(stage => revisions[stage] == "original"), "Earlier stages remain reusable");
                Check(affectedStages.All(stage => revisions[stage] != "original"), "Selected and later stages must rebuild");
            });
            tests.Run("Invalid reset arguments leave the project unchanged", () =>
            {
                var before = File.ReadAllBytes(store.PathFor("project.bin"));
                Check(application.Run(["--project", root, "pipeline", "reset"]) == 2, "Missing --from is rejected");
                Check(application.Run(["--project", root, "pipeline", "reset", "--from"]) == 2, "Missing stage value is rejected");
                Check(application.Run(["--project", root, "pipeline", "reset", "--from", "unknown"]) == 2, "Unknown stage is rejected");
                Check(before.SequenceEqual(File.ReadAllBytes(store.PathFor("project.bin"))), "Invalid commands must not edit the project");
            });
            tests.Run("Reset command cannot invalidate stages while a run holds the project lock", () =>
            {
                var before = File.ReadAllBytes(store.PathFor("project.bin"));
                using var lease = store.Lock("run");
                Check(application.Run(["--project", root, "pipeline", "reset", "--from", "features"]) == 6, "Active processing must reject reset");
                Check(before.SequenceEqual(File.ReadAllBytes(store.PathFor("project.bin"))), "A busy project must remain unchanged");
            });
        }
        finally
        {
            Directory.Delete(root, true);
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
