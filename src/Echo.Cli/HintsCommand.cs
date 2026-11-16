using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Projects;

namespace Echo.Cli;

[Export(typeof(ICliCommand)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class HintsCommand : ICliCommand
{
    private readonly IProjectStoreFactory storeFactory;
    private readonly ICommandOutput output;
    private readonly IJsonCodec jsonCodec;
    private readonly IHintFactory hintFactory;

    [ImportingConstructor]
    public HintsCommand(IProjectStoreFactory storeFactory, ICommandOutput output, IJsonCodec jsonCodec, IHintFactory hintFactory)
    {
        this.storeFactory = storeFactory;
        this.output = output;
        this.jsonCodec = jsonCodec;
        this.hintFactory = hintFactory;
    }

    public IReadOnlyCollection<string> Names => ["hints"];

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
        output.Write(new ProjectRevisionResult
        {
            Id = project.Id,
            Revision = project.Revision
        });
    }
}
