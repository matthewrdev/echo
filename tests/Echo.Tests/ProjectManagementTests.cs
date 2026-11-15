using System.ComponentModel.Composition;
using Echo.Cli;
using Echo.Projects;

namespace Echo.Tests;

[Export, PartCreationPolicy(CreationPolicy.Shared)]
public sealed class ProjectManagementTests
{
    private readonly ICommandApplication application;
    private readonly IProjectStoreFactory storeFactory;

    [ImportingConstructor]
    public ProjectManagementTests(ICommandApplication application, IProjectStoreFactory storeFactory)
    {
        this.application = application;
        this.storeFactory = storeFactory;
    }

    internal void Run(TestSuite tests)
    {
        var root = Path.Combine(Path.GetTempPath(), "echo-project-management-" + Guid.NewGuid().ToString("N"));
        var store = storeFactory.Open(root);
        store.Init("Original");
        var id = store.Load().Id;
        var artifact = store.PathFor("artifacts/retained.txt");
        File.WriteAllText(artifact, "saved mesh");
        store.Edit(project => project.StageRevisions["mesh"] = "retained");
        try
        {
            tests.Run("Rename persists the display name while preserving identity, path and saved artifacts", () =>
            {
                Check(application.Run(["--project", root, "project", "rename", "--name", "  New name  "]) == 0, "Rename succeeds");
                var saved = store.Load();
                Check(saved.Name == "New name" && saved.Id == id, "Only the name changes");
                Check(saved.StageRevisions["mesh"] == "retained", "Mesh fingerprint is unchanged");
                Check(File.ReadAllText(artifact) == "saved mesh", "Saved artifacts remain in place");
            });
            tests.Run("Invalid names and unconfirmed deletion leave the project unchanged", () =>
            {
                var before = File.ReadAllBytes(store.PathFor("project.json"));
                Check(application.Run(["--project", root, "project", "rename", "--name", " "]) == 2, "Blank name is rejected");
                Check(application.Run(["--project", root, "project", "rename", "--name", "Bad\nname"]) == 2, "Control characters are rejected");
                Check(application.Run(["--project", root, "project", "delete"]) == 2, "Missing confirmation is rejected");
                Check(application.Run(["--project", root, "project", "delete", "--confirm", "wrong-id"]) == 2, "Stale confirmation is rejected");
                Check(before.SequenceEqual(File.ReadAllBytes(store.PathFor("project.json"))), "Manifest is unchanged");
            });
            tests.Run("Project operations reject active processing and deletion rejects active editing or import", () =>
            {
                foreach (var operation in new[] { "run", "import", "edit" })
                {
                    using var lease = store.Lock(operation);
                    Check(application.Run(["--project", root, "project", "delete", "--confirm", id]) == 6, "Busy deletion is rejected");
                    Check(File.Exists(artifact), "Busy project remains intact");
                    if (operation == "run")
                    {
                        Check(application.Run(["--project", root, "project", "rename", "--name", "Busy"]) == 6, "Busy rename is rejected");
                    }
                }
            });
            tests.Run("Confirmed deletion removes the entire project and preserves files outside it", () =>
            {
                var outside = root + "-original.jpg";
                File.WriteAllText(outside, "original photo");
                try
                {
                    File.CreateSymbolicLink(store.PathFor("sources/linked-photo.jpg"), outside);
                    Check(application.Run(["--project", root, "project", "delete", "--confirm", id]) == 0, "Delete succeeds");
                    Check(!Directory.Exists(root), "Whole project folder is removed");
                    Check(File.ReadAllText(outside) == "original photo", "External source is preserved");
                }
                finally
                {
                    File.Delete(outside);
                }
            });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
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
