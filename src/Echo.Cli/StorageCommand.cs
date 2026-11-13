using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Pipeline;
using Echo.Projects;

namespace Echo.Cli;

[Export(typeof(ICliCommand)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class StorageCommand : ICliCommand
{
    private readonly IProjectStoreFactory stores;
    private readonly IArtifactStorageService storage;
    private readonly ICommandOutput output;

    [ImportingConstructor]
    public StorageCommand(IProjectStoreFactory stores, IArtifactStorageService storage, ICommandOutput output)
    {
        this.stores = stores;
        this.storage = storage;
        this.output = output;
    }

    public IReadOnlyCollection<string> Names => ["storage"];

    public int Execute(Arguments args)
    {
        var store = stores.Open(args.Get("project", null) ?? throw new EchoException("--project PATH is required.", 2));
        var artifact = args.Get("artifact", null);
        switch (args.Word(1, "list"))
        {
            case "list":
                output.Write(storage.Inspect(store));
                break;
            case "plan":
                output.Write(storage.Plan(store, artifact));
                break;
            case "delete":
                var token = args.Get("confirm", null) ?? throw new EchoException("Review storage plan, then provide its token with --confirm TOKEN.", 2);
                output.Write(storage.Delete(store, artifact, token));
                break;
            default:
                throw new EchoException("Use storage list, plan, or delete. Omit --artifact to plan a complete purge.", 2);
        }
        return 0;
    }
}
