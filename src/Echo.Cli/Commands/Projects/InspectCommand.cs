using Echo.Infrastructure;
using Echo.Models;
using Echo.Pipeline.Runs;
using Echo.Pipeline;
using Echo.Projects;

namespace Echo.Cli.Commands.Projects;

public sealed class InspectCommand : ICliCommand
{
    private readonly IProjectStoreFactory storeFactory;
    private readonly ICommandOutput output;
    private readonly IProjectInspector inspector;
    private readonly IFileOperations files;

    public InspectCommand(IProjectStoreFactory storeFactory, ICommandOutput output, IProjectInspector inspector, IFileOperations files)
    {
        this.storeFactory = storeFactory;
        this.output = output;
        this.inspector = inspector;
        this.files = files;
    }

    public IReadOnlyCollection<string> Names => ["inspect", "geo"];

    public IReadOnlyList<CommandUsage> Usage =>
    [
        new("inspect",
            "--project PATH inspect [--run RUN_ID] [--summary] [--known-project TOKEN] [--known-snapshot TOKEN]",
            "Inspect project reconstruction and georeference data."),
        new("geo",
            "--project PATH geo [--run RUN_ID] [--summary]",
            "Alias for inspect."),
    ];

    public int Execute(Arguments args)
    {
        var store = storeFactory.Open(args.Get("project", null) ?? throw new EchoException("--project PATH is required.", 2));
        var inspection = inspector.Inspect(store, args.Get("run", "latest")!, args.Options.ContainsKey("summary"));
        var knownProject = args.Get("known-project", null);
        var knownSnapshot = args.Get("known-snapshot", null);
        if (knownProject == null && knownSnapshot == null)
        {
            output.Write(inspection);
            return 0;
        }

        var projectToken = FormattableString.Invariant($"{inspection.Project.Id}:{inspection.Project.Revision}");
        var run = inspection.Run;
        // A run may apply temporary settings without changing its project revision.
        var snapshotToken = run == null ? "" : $"{run.Id}:{files.Hash(run.Snapshot.Settings)}";
        output.Write(new WorkspaceInspection
        {
            SchemaVersion = inspection.SchemaVersion,
            ProjectRoot = inspection.ProjectRoot,
            Project = projectToken == knownProject ? null : inspection.Project,
            Run = run == null ? null : new WorkspaceRunSnapshot
            {
                Id = run.Id,
                Status = run.Status,
                Target = run.Target,
                Snapshot = snapshotToken == knownSnapshot ? null : run.Snapshot,
                SnapshotToken = snapshotToken,
                Artifacts = run.Artifacts,
                Error = run.Error,
                Progress = run.Progress,
                StorageVersion = run.StorageVersion
            },
            Reconstruction = inspection.Reconstruction,
            Surface = inspection.Surface,
            Cloud = inspection.Cloud,
            Warnings = inspection.Warnings
        });
        return 0;
    }
}
