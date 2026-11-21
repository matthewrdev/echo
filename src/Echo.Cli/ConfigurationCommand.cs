using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Projects;

namespace Echo.Cli;

public sealed class ConfigurationCommand : ICliCommand
{
    private readonly IProjectStoreFactory storeFactory;
    private readonly ICommandOutput output;
    private readonly IJsonCodec jsonCodec;

    public ConfigurationCommand(IProjectStoreFactory storeFactory, ICommandOutput output, IJsonCodec jsonCodec)
    {
        this.storeFactory = storeFactory;
        this.output = output;
        this.jsonCodec = jsonCodec;
    }

    public IReadOnlyCollection<string> Names => ["config"];

    public int Execute(Arguments args)
    {
        var action = args.Word(1, "");
        var ndjson = args.IsNdjson;
        var store = storeFactory.Open(args.Get("project", null) ?? throw new EchoException("--project PATH is required.", 2));
        store.Load();
        switch (action)
        {
            case "show":
                output.Write(store.Load().Settings);
                return 0;
            case "import":
                var settings = jsonCodec.Read<Settings>(args.RequiredWord(2));
                settings.Validate();
                WriteReceipt(store.Edit(p => p.Settings = settings));
                return 0;
            default:
                throw new EchoException("Unknown config action. Use --help.", 2);
        }
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
