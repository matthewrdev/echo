using Echo.Models;
using Echo.Infrastructure;
using Echo.Projects;

namespace Echo.Cli.Commands.Hints;

public sealed class HintsCommand : ICliCommand
{
    private readonly IProjectStoreFactory storeFactory;
    private readonly ICommandOutput output;
    private readonly IJsonCodec jsonCodec;
    private readonly IHintFactory hintFactory;

    public HintsCommand(IProjectStoreFactory storeFactory, ICommandOutput output, IJsonCodec jsonCodec, IHintFactory hintFactory)
    {
        this.storeFactory = storeFactory;
        this.output = output;
        this.jsonCodec = jsonCodec;
        this.hintFactory = hintFactory;
    }

    public IReadOnlyCollection<string> Names => ["hints"];

    public IReadOnlyList<CommandUsage> Usage =>
    [
        new("hints list",
            "--project PATH hints list",
            "List saved reconstruction hints."),
        new("hints import",
            "--project PATH hints import FILE",
            "Add hints from a JSON file."),
        new("hints camera",
            "--project PATH hints camera IMAGE_ID [--lat N --lon N --alt N --alt-datum TEXT] [--yaw N --pitch N --roll N] [--uncertainty N]",
            "Add a camera position or attitude hint."),
        new("hints overlap",
            "--project PATH hints overlap IMAGE_ID OTHER_ID",
            "Tell image matching that two photos overlap."),
        new("hints tie",
            "--project PATH hints tie IMAGE_ID OTHER_ID --ax X --ay Y --bx X --by Y",
            "Identify the same point in two photos."),
        new("hints disable",
            "--project PATH hints disable HINT_ID",
            "Disable a saved hint."),
    ];

    public int Execute(Arguments args)
    {
        var action = args.Word(1, "");
        var ndjson = args.IsNdjson;
        var store = storeFactory.Open(args.Get("project", null) ?? throw new EchoException("--project PATH is required.", 2));
        store.Load();
        if (action == "list")
        {
            output.Write(store.Load().Hints);
            return 0;
        }

        if (action == "disable")
        {
            WriteReceipt(store.Edit(p => (p.Hints.Find(h => h.Id == args.RequiredWord(2)) ?? throw new EchoException("Unknown hint.")).Enabled = false));
            return 0;
        }

        var hints = action == "import" ? jsonCodec.Read<List<Hint>>(args.RequiredWord(2)) : [hintFactory.Create(args, action)];
        store.AddHints(hints);
        output.Write(hints);
        return 0;
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
