using Echo.Cli;
using Echo.Models;
using Echo.Infrastructure;
using Echo.Projects;

namespace Echo.Tests;

public sealed class ProjectManagementTests
{
    private readonly ICommandApplication application;
    private readonly IProjectStoreFactory storeFactory;
    private readonly IFileOperations fileOperations;

    public ProjectManagementTests(ICommandApplication application, IProjectStoreFactory storeFactory, IFileOperations fileOperations)
    {
        this.application = application;
        this.storeFactory = storeFactory;
        this.fileOperations = fileOperations;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Confirmed batch photo deletion preserves saved run inputs and removes unused copies", VerifyPhotoDeletion);
        var root = Path.Combine(Path.GetTempPath(), "echo-project-management-" + Guid.NewGuid().ToString("N"));
        var store = storeFactory.Open(root);
        store.Init("Original");
        var id = store.Load().Id;
        var artifact = store.PathFor("artifacts/retained.txt");
        File.WriteAllText(artifact, "saved mesh");
        store.Edit(project => project.StageRevisions["mesh"] = "retained");
        try
        {
            tests.Run("Project info and deletion do not depend on saved run or artifact formats", () =>
            {
                var savedRun = store.PathFor("runs/obsolete");
                Directory.CreateDirectory(savedRun);
                File.WriteAllText(Path.Combine(savedRun, "run.json"), "unreadable old run");
                Check(application.Run(["--project", root, "project", "info"]) == 0, "Project identity can be read independently");
            });
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

    private void VerifyPhotoDeletion()
    {
        var scratch = Directory.CreateTempSubdirectory("echo-photo-deletion-");
        try
        {
            var store = storeFactory.Open(Path.Combine(scratch.FullName, "test.echo"));
            store.Init();
            var saved = MakePhoto(store, "saved");
            var spare = MakePhoto(store, "spare");
            var keep = MakePhoto(store, "keep");
            store.Edit(project => project.Images.AddRange([saved, keep]));
            var runId = Guid.NewGuid().ToString("N");
            var runFolder = store.PathFor($"runs/{runId}");
            Directory.CreateDirectory(runFolder);
            fileOperations.AtomicJson(Path.Combine(runFolder, "run.json"),
                new RunData { Id = runId, Status = "completed", Snapshot = store.Load() });
            store.Edit(project =>
            {
                project.Images.Add(spare);
                project.Images.Single(photo => photo.Id == keep.Id).DuplicateOf = saved.Id;
                project.Hints.Add(new Hint { ImageId = keep.Id, OtherImageId = saved.Id, Type = "tie" });
            });

            string[] Delete(params string[] rest) => ["--project", store.Root, "images", "delete", .. rest];
            var projectId = store.Load().Id;
            Check(application.Run(Delete(saved.Id, spare.Id)) == 2, "Reject an unconfirmed deletion");
            Check(application.Run(Delete(spare.Id, "unknown", "--confirm", projectId)) == 2, "Reject stale selections");
            using (store.Lock("run"))
            {
                Check(application.Run(Delete(saved.Id, spare.Id, "--confirm", projectId)) == 6, "Block deletion during a run");
            }

            Check(application.Run(Delete(saved.Id, spare.Id, "--confirm", projectId)) == 0, "Delete both inputs in one operation");
            var active = store.Load();
            Check(active.Images.Single().Id == keep.Id && active.Images.Single().DuplicateOf == null, "Remove photos and repair the duplicate reference");
            Check(active.Hints.Count == 0, "Remove hints whose secondary photo was deleted");
            Check(File.Exists(store.PathFor(saved.Source)) && File.Exists(store.PathFor(saved.Preview)), "Keep files needed by prior runs");
            Check(!File.Exists(store.PathFor(spare.Source)) && !File.Exists(store.PathFor(spare.Preview)), "Remove unused input copies");
            Check(!File.Exists(store.PathFor("sources/spare.original.heic")), "Remove the imported original copy");
            Check(File.Exists(store.PathFor(keep.Source)) && store.GetRun(runId).Snapshot.Images.Count == 2, "Preserve live files and historical inputs");
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    private Photo MakePhoto(IProjectStore store, string id)
    {
        var photo = new Photo { Id = id, Source = $"sources/{id}.jpg", Preview = $"previews/{id}.jpg" };
        File.WriteAllText(store.PathFor(photo.Source), id);
        File.WriteAllText(store.PathFor(photo.Preview), id);
        File.WriteAllText(store.PathFor($"sources/{id}.original.heic"), id);
        return photo;
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
