using Echo.Cli;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Projects;

namespace Echo.Tests;

public sealed class RunScriptingTests
{
    private readonly ICommandApplication application;
    private readonly IProjectStoreFactory stores;
    private readonly IFileOperations files;

    public RunScriptingTests(ICommandApplication application, IProjectStoreFactory stores, IFileOperations files)
    {
        this.application = application;
        this.stores = stores;
        this.files = files;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("CLI settings apply to the run snapshot and leave the project settings intact", () =>
        {
            var scratch = Directory.CreateTempSubdirectory("echo-run-settings-");
            try
            {
                var store = stores.Open(Path.Combine(scratch.FullName, "test.echo"));
                store.Init("Run settings test");
                var before = store.Load();
                var settingsPath = Path.Combine(scratch.FullName, "settings.json");
                files.AtomicJson(settingsPath, new Settings { FeatureWidth = 500 });

                // A project without images fails source validation after saving the run snapshot.
                var result = application.Run(["--project", store.Root, "run", "start", "--target", "features",
                    "--settings", settingsPath, "--events", "none"]);
                var run = store.GetRun("latest");
                var after = store.Load();
                var savedRunSettings = result == 3 && run.Status == "failed" && run.Snapshot.Settings.FeatureWidth == 500;
                var keptProjectSettings = after.Settings.FeatureWidth == before.Settings.FeatureWidth && after.Revision == before.Revision;
                if (!savedRunSettings || !keptProjectSettings)
                {
                    throw new InvalidOperationException("Expected the settings override to affect only the run snapshot.");
                }

                var resume = application.Run(["--project", store.Root, "run", "resume", run.Id, "--settings", settingsPath]);
                if (resume != 2)
                {
                    throw new InvalidOperationException("Expected resume to reject a replacement settings file.");
                }
            }
            finally
            {
                scratch.Delete(recursive: true);
            }
        });
    }
}
