using Echo.Models;
using Echo.Pipeline;
using Echo.Projects;

namespace Echo.Cli;

public sealed class InspectCommand : ICliCommand
{
    private readonly IProjectStoreFactory storeFactory;
    private readonly ICommandOutput output;
    private readonly IProjectInspector inspector;

    public InspectCommand(IProjectStoreFactory storeFactory, ICommandOutput output, IProjectInspector inspector)
    {
        this.storeFactory = storeFactory;
        this.output = output;
        this.inspector = inspector;
    }

    public IReadOnlyCollection<string> Names => ["inspect", "geo"];

    public int Execute(Arguments args)
    {
        var store = storeFactory.Open(args.Get("project", null) ?? throw new EchoException("--project PATH is required.", 2));
        store.Load();
        output.Write(inspector.Inspect(store, args.Get("run", "latest")!, args.Options.ContainsKey("summary")));
        return 0;
    }
}
