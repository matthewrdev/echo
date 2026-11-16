using System.ComponentModel.Composition;
using Echo.Cli;
using Echo.Contracts;
using Echo.Depth;
using Echo.Infrastructure;
using Echo.Pipeline;
using Echo.Projects;

namespace Echo.Tests;

[Export]
public sealed class RunRecoveryTests
{
    private readonly IRunStateRecovery recovery;
    private readonly IProjectStoreFactory stores;
    private readonly IFileOperations files;
    private readonly ICommandApplication application;
    private readonly IRunContextFactory contexts;
    private readonly IDepthExecutionPlanner depthPlanner;

    [ImportingConstructor]
    public RunRecoveryTests(IRunStateRecovery recovery, IProjectStoreFactory stores,
        IFileOperations files, ICommandApplication application, IRunContextFactory contexts, IDepthExecutionPlanner depthPlanner)
    {
        this.recovery = recovery;
        this.stores = stores;
        this.files = files;
        this.application = application;
        this.contexts = contexts;
        this.depthPlanner = depthPlanner;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Progress reports percentage changes at up to eight updates per second", () =>
        {
            var filter = new OperationProgressFilter();
            var accepted = new List<int>();
            for (var count = 0; count <= 10000; count++)
            {
                if (filter.Accept("mesh", new OperationProgress("Edges", "Indexing", count, 10000), TimeSpan.FromMilliseconds(count)))
                {
                    accepted.Add(count);
                }
            }
            Check(accepted.First() == 0 && accepted.Last() == 10000, "Start and final completion must always be published");
            Check(accepted.Count <= 82, "Progress exceeded eight updates per second plus start/completion");
            for (var index = 1; index < accepted.Count - 1; index++)
            {
                Check(accepted[index] / 100 > accepted[index - 1] / 100, "Repeated the same whole percentage");
                Check(accepted[index] - accepted[index - 1] >= 125, "Progress exceeded the rate limit");
            }
        });
        tests.Run("Unknown totals are rate limited while operation changes and counter resets remain immediate", () =>
        {
            var filter = new OperationProgressFilter();
            var published = 0;
            for (var count = 0; count < 10000; count++)
            {
                if (filter.Accept("mesh", new OperationProgress("Holes", $"Boundary {count}", count), TimeSpan.FromMilliseconds(count)))
                {
                    published++;
                }
            }
            Check(published == 80, "Unknown totals must publish at most eight updates per second");
            var now = TimeSpan.FromSeconds(10);
            Check(filter.Accept("mesh", new OperationProgress("Compact", "Compacting", 0, 100), now), "Operation change was lost");
            Check(filter.Accept("mesh", new OperationProgress("Compact", "Complete", 100, 100), now), "Completion was delayed");
            Check(!filter.Accept("mesh", new OperationProgress("Compact", "Complete", 100, 100), now), "Duplicate completion was emitted");
            Check(filter.Accept("mesh", new OperationProgress("Compact", "Restarted", 0, 100), now), "Repeated pass did not reset");
        });
        tests.Run("A slow counted operation provides a heartbeat without repeating unchanged progress", () =>
        {
            var filter = new OperationProgressFilter();
            filter.Accept("mesh", new OperationProgress("Edges", "Indexing", 0, 1000000), TimeSpan.Zero);
            Check(!filter.Accept("mesh", new OperationProgress("Edges", "Indexing", 1, 1000000), TimeSpan.FromSeconds(1)), "Sub-percent work was spammed");
            Check(filter.Accept("mesh", new OperationProgress("Edges", "Indexing", 2, 1000000), TimeSpan.FromSeconds(5)), "Slow work needs a heartbeat");
            Check(!filter.Accept("mesh", new OperationProgress("Edges", "Indexing", 2, 1000000), TimeSpan.FromSeconds(10)), "Unchanged work was reported again");
        });
        tests.Run("Reused prerequisites are reported as checking, not processing again", () =>
        {
            WithProject(store =>
            {
                var first = contexts.Create(store, SaveRun(store), default, null);
                first.Start();
                var artifact = first.Build("feature-image", new
                {
                    sample = 1
                }, directory => File.WriteAllText(Path.Combine(directory, "data"), "saved"));
                first.Report("imageCheckpoint", new
                {
                    stage = "features",
                    completed = 1,
                    total = 2
                });
                Check(first.Run.Progress?.Phase == "processing", "An actual build must report processing");
                var second = contexts.Create(store, SaveRun(store), default, null);
                second.Start();
                var reused = second.Build("feature-image", new
                {
                    sample = 1
                }, _ => throw new InvalidOperationException("A cached stage must not execute again"));
                second.Report("imageCheckpoint", new
                {
                    stage = "features",
                    completed = 1,
                    total = 2
                });
                Check(reused == artifact && second.Run.Progress?.Phase == "checking", "Saved work must report a reuse check");
                Check(second.Run.Progress?.Message.StartsWith("Reusing saved features", StringComparison.Ordinal) == true, "Explain the completed prerequisite check");
                second.Build("feature-image", new
                {
                    sample = 2
                }, directory => File.WriteAllText(Path.Combine(directory, "data"), "new"));
                second.Report("imageCheckpoint", new
                {
                    stage = "features",
                    completed = 2,
                    total = 2
                });
                Check(second.Run.Progress?.Phase == "processing", "A changed input must switch back to actual processing");
            });
        });
        tests.Run("Child checkpoint starts preserve algorithm progress and completed work", () => WithProject(store =>
        {
            using var context = contexts.Create(store, SaveRun(store), default, null);
            context.Report("stageStarted", new
            {
                stage = "alignment"
            });
            context.ReportOperation("alignment", new OperationProgress("Guided matching", "Validating overlap", 532, 1682, "image pairs"));
            var advanced = context.Run.Progress!.Updated;
            context.Report("stageStarted", new
            {
                stage = "alignment-guided-pair"
            });
            Check(context.Run.Progress.Completed == 532 && context.Run.Progress.Total == 1682, "Child checkpoint erased parent work counters");
            Check(context.Run.Progress.Message == "Validating overlap" && context.Run.Progress.Operation == "Guided matching", "Child checkpoint hid the active algorithm");
            Check(context.Run.Progress.Updated == advanced, "Checkpoint start falsely reported completed work");
            context.BeginOperation("alignment", "Camera refinement");
            Check(context.Run.Progress.Completed == null, "A real operation transition must reset counters");
        }));
        tests.Run("Substeps reset counters and resource heartbeats preserve last work time", () => WithProject(store =>
        {
            using var context = contexts.Create(store, SaveRun(store), default, null);
            context.ReportOperation("mesh", new OperationProgress("Surface extraction", "Extracting", 4000000000, 5000000000, "cells"));
            Check(context.Run.Progress?.Completed == 4000000000 && context.Run.Progress.Unit == "cells", "Large work counters were truncated");
            context.BeginOperation("mesh", "Hole repair");
            Check(context.Run.Progress?.Completed == null && context.Run.Progress?.Total == null, "Previous percentage leaked into a different operation");
            context.BeginStep("mesh", ["Prepare", "Collapse"], 1, 100);
            context.BeginOperation("mesh", "Saving mesh");
            Check(context.Run.Progress!.Steps.Length == 0 && context.Run.Progress.StepIndex == null,
                "Finished simplification steps leaked into the next operation");
            var updated = context.Run.Progress!.Updated;
            context.ReportResources();
            Check(context.Run.Progress.Updated == updated && context.Run.Progress.Resources != null, "Resource sampling falsely reported computational progress");
        }));
        tests.Run("Text-only updates preserve counted operation progress and units", () => WithProject(store =>
        {
            using var context = contexts.Create(store, SaveRun(store), default, null);
            context.ReportOperation("mesh", new OperationProgress("Surface extraction", "Extracting", 4000000000, 5000000000, "lattice cells"));
            context.ReportProgress("mesh", "Extracting surface: more triangles accepted");
            var progress = context.Run.Progress!;
            Check(progress.Completed == 4000000000 && progress.Total == 5000000000, "Text update erased counted progress");
            Check(progress.Unit == "lattice cells" && progress.Operation == "Surface extraction", "Text update changed the operation units");
            var saved = store.GetRun(context.Run.Id).Progress!;
            Check(saved.Completed == progress.Completed && saved.Total == progress.Total, "Polling would see different progress from the live operation");
            context.BeginOperation("mesh", "Geometry simplification");
            Check(context.Run.Progress!.Completed == null && context.Run.Progress.Total == null, "A genuine new operation must reset its counters");
        }));
        tests.Run("Mesh stage plan survives operation transitions, text messages and polling", () => WithProject(store =>
        {
            using var context = contexts.Create(store, SaveRun(store), default, null);
            string[] phases = ["Load inputs", "Extract surface", "Simplify geometry", "Save mesh"];
            context.ReportOperation("mesh", new OperationProgress("Load inputs", "Loading")
            {
                StageSteps = phases,
                StageStepIndex = 0
            });
            Check(store.GetRun(context.Run.Id).Progress!.StageSteps.SequenceEqual(phases), "Stage plan is missing before the first counted operation");
            context.ReportOperation("mesh", new OperationProgress("Extracting", "Extracting", 40, 100, "cells")
            {
                StageSteps = phases,
                StageStepIndex = 1
            });
            context.ReportProgress("mesh", "More triangles accepted");
            Check(context.Run.Progress!.StageStepIndex == 1 && context.Run.Progress.Completed == 40, "Text update reset the phase or counter");
            context.ReportOperation("mesh", new OperationProgress("Collapse edges", "Collapsing", 3, 10, "edges")
            {
                StageSteps = phases,
                StageStepIndex = 2,
                Steps = ["Prepare", "Collapse edges", "Compact"],
                StepIndex = 1
            });
            var saved = store.GetRun(context.Run.Id).Progress!;
            Check(saved.StageStepIndex == 2 && saved.StepIndex == 1 && saved.StageSteps.Length == 4 && saved.Steps.Length == 3, "Polling lost nested simplification progress");
            context.ReportOperation("mesh", new OperationProgress("Save mesh", "Saving")
            {
                StageSteps = phases,
                StageStepIndex = 3,
                Steps = []
            });
            Check(context.Run.Progress!.StageStepIndex == 3 && context.Run.Progress.Steps.Length == 0 && context.Run.Progress.Completed == null, "Nested steps leaked into saving");
        }));
        tests.Run("Resume can change operational budgets without changing reconstruction settings", () => WithProject(store =>
        {
            var run = SaveRun(store);
            var width = run.Snapshot.Settings.DepthWidth;
            store.Edit(project =>
            {
                project.Settings.ProcessingMemoryBudgetMb = 1024;
                project.Settings.ProcessingDiskBudgetMb = 2048;
                project.Settings.DepthWidth = width + 16;
                project.Settings.DepthMemoryBudgetMb = 512;
            });
            using var context = contexts.Create(store, run, default, null);
            Check(context.Resources.MemoryBytes == 1024L * 1048576 && context.Resources.DiskBytes == 2048L * 1048576, "Resume ignored updated operational limits");
            Check(context.Resources.DepthMemoryBudgetMb == 512, "Resume ignored the updated depth scheduling budget");
            Check(context.Run.Snapshot.Settings.DepthWidth == width, "Resource changes modified reconstruction inputs");
        }));
        tests.Run("Depth admission identifies the limiting budget and accepts a scheduling-only override", () =>
        {
            var project = new ProjectData
            {
                Images = [new Photo { Width = 4032, Height = 2268 }],
                Settings = new Settings { DepthConcurrency = 2, DepthWidth = 2048, DepthPlanes = 192, DepthNeighbours = 12, DepthWorkerThreads = 16, DepthMemoryBudgetMb = 1024 }
            };
            try
            {
                depthPlanner.Plan(project, 190, 16L * 1024 * 1048576);
                throw new InvalidOperationException("Underfunded depth job was admitted");
            }
            catch (EchoException error)
            {
                Check(error.Message.Contains("DepthMemoryBudgetMb", StringComparison.Ordinal), "Identify the depth-specific limit");
            }
            var plan = depthPlanner.Plan(project, 190, 16L * 1024 * 1048576, 4096);
            Check(plan.ConcurrentImages == 2 && plan.MemoryBudgetBytes == 4096L * 1048576, "Updated depth budget was not used");
            var automatic = depthPlanner.Plan(project, 190, 24L * 1024 * 1048576, 0);
            Check(automatic.MemoryBudgetBytes == 24L * 1024 * 1048576 && automatic.ConcurrentImages == 2,
                "Automatic depth must use the already-reserved job budget, not an obsolete per-stage cap");
            Check(automatic.ConcurrentImages * automatic.EstimatedBytesPerImage <= automatic.MemoryBudgetBytes,
                "The scheduled images must fit their resolved budget");
            Check(project.Settings.DepthMemoryBudgetMb == 1024 && project.Settings.DepthWidth == 2048, "Scheduling override mutated frozen reconstruction inputs");
            try
            {
                depthPlanner.Plan(project, 190, 256L * 1048576, 4096);
                throw new InvalidOperationException("Underfunded whole job was admitted");
            }
            catch (EchoException error)
            {
                Check(error.Message.Contains("ProcessingMemoryBudgetMb", StringComparison.Ordinal), "Identify the whole-job limit");
            }
        });
        tests.Run("Stop reconciles an exited worker and retains completed checkpoints", () =>
        {
            WithProject(store =>
            {
                var run = SaveRun(store);
                Check(application.Run(["--project", store.Root, "run", "cancel", run.Id]) == 0, "Stop command must succeed");
                Check(store.GetRun(run.Id).Status == "cancelled", "An absent worker must not remain stopping forever");
                Check(File.ReadAllText(store.PathFor(run.Artifacts["depth"] + "/saved.txt")) == "retained", "Saved output must survive cancellation");
            });
        });
        tests.Run("Run status detects an exited worker without mistaking a quiet live worker for a failure", () =>
        {
            WithProject(store =>
            {
                var run = SaveRun(store);
                using (store.Lock("run"))
                {
                    recovery.Recover(store);
                    Check(store.GetRun(run.Id).Status == "running", "A live lease protects the run regardless of progress age");
                }
                recovery.Recover(store);
                var recovered = store.GetRun(run.Id);
                Check(recovered.Status == "failed", "An orphaned run must become resumable");
                Check(recovered.Error?.Contains("exited unexpectedly") == true, "Explain why processing stopped");
                var events = File.ReadAllText(store.PathFor($"runs/{run.Id}/events.ndjson"));
                recovery.Recover(store);
                Check(events == File.ReadAllText(store.PathFor($"runs/{run.Id}/events.ndjson")), "Polling must not duplicate recovery events");
            });
        });
    }

    private RunData SaveRun(IProjectStore store)
    {
        var run = new RunData { Id = Guid.NewGuid().ToString("N"), Snapshot = store.Load(), Target = "depth" };
        var artifact = "artifacts/depth/completed";
        Directory.CreateDirectory(store.PathFor(artifact));
        File.WriteAllText(store.PathFor(artifact + "/saved.txt"), "retained");
        run.Artifacts["depth"] = artifact;
        files.AtomicJson(store.PathFor($"runs/{run.Id}/run.json"), run);
        return run;
    }

    private void WithProject(Action<IProjectStore> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "echo-run-recovery-" + Guid.NewGuid().ToString("N"));
        var store = stores.Open(root);
        store.Init("Run recovery regression");
        try
        {
            action(store);
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
