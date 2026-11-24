using Echo.Cli.Commands.Geography;
using Echo.Models;
using Echo.Pipeline.Exporting;
using Echo.Projects;
using Echo.Rendering;

namespace Echo.Cli.Commands.Export;

internal sealed class GisCommand : ICliCommand
{
    private readonly IProjectStoreFactory stores;
    private readonly IGisExportService exporter;
    private readonly ICommandOutput output;
    private readonly IGeographicRunModelSource models;
    private readonly INativeToolProcess native;

    public GisCommand(IProjectStoreFactory stores, IGisExportService exporter, ICommandOutput output,
        IGeographicRunModelSource models, INativeToolProcess native)
    {
        this.stores = stores;
        this.exporter = exporter;
        this.output = output;
        this.models = models;
        this.native = native;
    }

    public IReadOnlyCollection<string> Names => ["export-gis"];

    public IReadOnlyList<CommandUsage> Usage =>
    [
        new("export-gis",
            "--project PATH export-gis --output NEW_DIRECTORY [--run RUN_ID]",
            "Export geographic artifacts to a new directory. On macOS, include a GeoTIFF model projection when a saved mesh is available."),
    ];

    public int Execute(Arguments arguments)
    {
        var project = arguments.Get("project", null) ?? throw new EchoException("--project PATH is required.", 2);
        var destination = arguments.Get("output", null) ?? throw new EchoException("--output NEW_DIRECTORY is required.", 2);
        var store = stores.Open(project);
        store.Load();
        output.Write(exporter.Export(store, arguments.Get("run", "latest")!, destination,
            (run, origin, directory) => WriteRaster(store, run, origin, directory)));
        return 0;
    }

    private void WriteRaster(IProjectStore store, RunData run, GeoPosition origin, string directory)
    {
        var source = models.Resolve(store, run, origin)
            ?? throw new EchoException("A saved model is required.", 5);
        var context = GeographicNativeJsonContext.Default;
        var written = native.Invoke("geographic-raster", new GeographicRasterRequest(source, directory),
            context.GeographicRasterRequest, context.Boolean);
        if (!written || !File.Exists(Path.Combine(directory, "model-map.tif")))
        {
            throw new EchoException("The native helper did not produce a geographic raster.", 5);
        }
    }
}
