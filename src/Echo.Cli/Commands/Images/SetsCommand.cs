using Echo.Models;
using Echo.Projects;

namespace Echo.Cli.Commands.Images;

public sealed class SetsCommand : ICliCommand
{
    private readonly IProjectStoreFactory storeFactory;
    private readonly ICommandOutput output;

    public SetsCommand(IProjectStoreFactory storeFactory, ICommandOutput output)
    {
        this.storeFactory = storeFactory;
        this.output = output;
    }

    public IReadOnlyCollection<string> Names => ["sets"];

    public IReadOnlyList<CommandUsage> Usage =>
    [
        new("sets list",
            "--project PATH sets list",
            "List image sets."),
        new("sets assign",
            "--project PATH sets assign IMAGE_ID --set NAME",
            "Assign a photo to an image set."),
    ];

    public int Execute(Arguments args)
    {
        var action = args.Word(1, "");
        var ndjson = args.IsNdjson;
        var store = storeFactory.Open(args.Get("project", null) ?? throw new EchoException("--project PATH is required.", 2));
        store.Load();
        switch (action)
        {
            case "list":
                output.Write(store.Load().Images.SelectMany(i => i.Sets).Distinct().Select(s => new
                {
                    name = s,
                    count = store.Load().Images.Count(i => i.Sets.Contains(s))
                }));
                return 0;
            case "assign":
                WriteReceipt(store.Edit(p =>
                {
                    var image = FindImage(p, args.RequiredWord(2));
                    var set = args.Get("set", null) ?? throw new EchoException("--set is required.", 2);
                    if (!image.Sets.Contains(set))
                    {
                        image.Sets.Add(set);
                    }
                }));
                return 0;
            default:
                throw new EchoException("Unknown sets action. Use --help.", 2);
        }
    }

    private Photo FindImage(ProjectData project, string id)
    {
        return project.Images.Find(image => image.Id == id) ?? throw new EchoException("Unknown image ID.", 2);
    }

    private void WriteReceipt(ProjectData project)
    {
        output.Write(new
        {
            project.Id,
            project.Revision
        });
    }
}
