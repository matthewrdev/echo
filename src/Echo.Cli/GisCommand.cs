using Echo.Contracts;
using Echo.Pipeline;
using Echo.Projects;

namespace Echo.Cli;

public sealed class GisCommand : ICliCommand
{
    private readonly IProjectStoreFactory stores;
    private readonly IGisExportService exporter;
    private readonly ICommandOutput output;

    public GisCommand(IProjectStoreFactory stores, IGisExportService exporter, ICommandOutput output)
    {
        this.stores = stores;
        this.exporter = exporter;
        this.output = output;
    }

    public IReadOnlyCollection<string> Names => ["export-gis"];

    public int Execute(Arguments arguments)
    {
        var project = arguments.Get("project", null) ?? throw new EchoException("--project PATH is required.", 2);
        var destination = arguments.Get("output", null) ?? throw new EchoException("--output NEW_DIRECTORY is required.", 2);
        var store = stores.Open(project);
        store.Load();
        output.Write(exporter.Export(store, arguments.Get("run", "latest")!, destination));
        return 0;
    }
}
