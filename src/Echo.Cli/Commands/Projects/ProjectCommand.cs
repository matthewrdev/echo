using Echo.Models;
using Echo.Projects;

namespace Echo.Cli.Commands.Projects;

public sealed class ProjectCommand : ICliCommand
{
    private readonly IProjectStoreFactory storeFactory;
    private readonly ICommandOutput output;
    private readonly IProjectManagementService management;

    public ProjectCommand(IProjectStoreFactory storeFactory, ICommandOutput output, IProjectManagementService management)
    {
        this.storeFactory = storeFactory;
        this.output = output;
        this.management = management;
    }

    public IReadOnlyCollection<string> Names => ["project"];

    public IReadOnlyList<CommandUsage> Usage =>
    [
        new("project init",
            "project init PATH [--name NAME]",
            "Create an Echo project."),
        new("project info",
            "--project PATH project info",
            "Read the project and its current revision."),
        new("project rename",
            "--project PATH project rename --name NAME",
            "Change the project display name."),
        new("project delete",
            "--project PATH project delete --confirm PROJECT_ID",
            "Delete the project after confirming its ID."),
    ];

    public int Execute(Arguments arguments)
    {
        var action = arguments.Word(1, "");
        if (action == "init")
        {
            var created = storeFactory.Open(arguments.RequiredWord(2));
            created.Init(arguments.Get("name", null));
            WriteProject(created);
            return 0;
        }

        var project = storeFactory.Open(arguments.Get("project", null)
            ?? throw new EchoException("--project PATH is required.", 2));
        switch (action)
        {
            case "info":
                output.Write(project.Load());
                return 0;
            case "rename":
                management.Rename(project, arguments.Get("name", null)
                    ?? throw new EchoException("--name NAME is required.", 2));
                WriteProject(project);
                return 0;
            case "delete":
                management.Delete(project, arguments.Get("confirm", null)
                    ?? throw new EchoException("--confirm PROJECT_ID is required.", 2));
                output.Write(new
                {
                    deleted = project.Root
                });
                return 0;
            default:
                throw new EchoException("Expected project init, info, rename or delete. Use --help.", 2);
        }
    }

    private void WriteProject(IProjectStore project)
    {
        output.Write(new
        {
            project = project.Root,
            data = project.Load()
        });
    }
}
