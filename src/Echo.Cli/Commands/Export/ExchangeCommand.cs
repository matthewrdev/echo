using Echo.Export;
using Echo.Models;
using Echo.Pipeline.Exporting;
using Echo.Projects;

namespace Echo.Cli.Commands.Export;

public sealed class ExchangeCommand : ICliCommand
{
    private readonly IProjectStoreFactory stores;
    private readonly IRunExchangeService exporter;
    private readonly ICommandOutput output;

    public ExchangeCommand(IProjectStoreFactory stores, IRunExchangeService exporter, ICommandOutput output)
    {
        this.stores = stores;
        this.exporter = exporter;
        this.output = output;
    }

    public IReadOnlyCollection<string> Names => ["export"];

    public IReadOnlyList<CommandUsage> Usage =>
    [
        new("export mesh",
            "--project PATH export mesh --format glb|glb-draco|gltf|obj|stl|dae|fbx|usdz|ply|3mf --output NEW_DIRECTORY [--run RUN_ID] [--size-mm MILLIMETRES]",
            "Export a saved mesh. GLB Draco verifies decoded UV triangle counts. 3MF uses a closed hull scaled to --size-mm along its longest axis. Other mesh exports include available georeference metadata."),
        new("export cloud",
            "--project PATH export cloud --format ply|pts|xyz|dxf|las|geo-las|laz|copc|e57 --output NEW_DIRECTORY [--run RUN_ID] [--crs EPSG:CODE]",
            "Export the saved dense cloud. LAZ, COPC, E57 and projected CRS exports use PDAL. LAS/LAZ/COPC support --crs EPSG:4978 or a projected EPSG code when the run has WGS84 ellipsoidal height."),
        new("export guide-tiles",
            "--project PATH export guide-tiles --output NEW_DIRECTORY [--run RUN_ID] [--tile-depth 1..4] [--detail-tile TILE_ID] [--leaf-texture-size PIXELS]",
            "Export a sparse hierarchy of guide-local GLBs. Bake a unique texture for every sector and leaf. Leaf sizes are 1024, 2048 or 4096 pixels, with 2048 as the default."),
        new("export images",
            "--project PATH export images --output NEW_DIRECTORY [--run RUN_ID]",
            "Export the saved run's enabled photos. Original files are preferred. photos.json identifies any normalized copies."),
    ];

    public int Execute(Arguments arguments)
    {
        var group = arguments.Word(1, "");
        var invalidArguments = arguments.Words.Count != 2
            || arguments.Options.Keys.Any(key => key is not ("project" or "output" or "format" or "run" or "json" or "crs" or "size-mm" or "tile-depth" or "detail-tile" or "leaf-texture-size"));
        var mismatchedOptions = group == "mesh" && arguments.Options.ContainsKey("crs")
            || group == "cloud" && arguments.Options.ContainsKey("size-mm")
            || group == "images" && new[] { "format", "crs", "size-mm" }.Any(arguments.Options.ContainsKey)
            || group == "guide-tiles" && new[] { "format", "crs", "size-mm" }.Any(arguments.Options.ContainsKey)
            || group != "guide-tiles" && new[] { "tile-depth", "detail-tile", "leaf-texture-size" }.Any(arguments.Options.ContainsKey);
        if (invalidArguments || mismatchedOptions)
        {
            throw new EchoException("Use export mesh, export cloud, export images, or export guide-tiles. See export --help.", 2);
        }

        var project = arguments.Get("project", null) ?? throw new EchoException("--project PATH is required.", 2);
        var destination = arguments.Get("output", null) ?? throw new EchoException("--output NEW_DIRECTORY is required.", 2);
        var format = arguments.Get("format", null)?.ToLowerInvariant();
        var run = arguments.Get("run", "latest")!;
        var store = stores.Open(project);
        store.Load();
        var result = group switch
        {
            "mesh" => exporter.ExportMesh(store, run, destination, MeshFormat(format ?? throw new EchoException("--format is required.", 2)),
                arguments.Options.ContainsKey("size-mm") ? arguments.Number("size-mm") : null),
            "cloud" => exporter.ExportPointCloud(store, run, destination, CloudFormat(format ?? throw new EchoException("--format is required.", 2)), arguments.Get("crs", null)),
            "images" => exporter.ExportImages(store, run, destination),
            "guide-tiles" => ExportGuideTiles(store, run, destination, arguments),
            _ => throw new EchoException("Use export mesh, export cloud, export images, or export guide-tiles. See export --help.", 2)
        };
        output.Write(result);
        return 0;
    }

    private RunExchangeResult ExportGuideTiles(IProjectStore store, string run, string destination, Arguments arguments)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            var leafTextureSize = arguments.Int("leaf-texture-size", 2048);
            return exporter.ExportGuideTiles(store, run, destination, arguments.Int("tile-depth", 2),
                arguments.Get("detail-tile", null), leafTextureSize, cancellation.Token);
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }

    private MeshExportFormat MeshFormat(string format) => format switch
    {
        "glb" => MeshExportFormat.Glb,
        "glb-draco" => MeshExportFormat.GlbDraco,
        "gltf" => MeshExportFormat.Gltf,
        "obj" => MeshExportFormat.Obj,
        "stl" => MeshExportFormat.Stl,
        "dae" => MeshExportFormat.Dae,
        "fbx" => MeshExportFormat.Fbx,
        "usdz" => MeshExportFormat.Usdz,
        "ply" => MeshExportFormat.Ply,
        "3mf" => MeshExportFormat.ThreeMf,
        _ => throw new EchoException("Unknown mesh format. See export mesh --help.", 2)
    };

    private PointCloudExportFormat CloudFormat(string format) => format switch
    {
        "ply" => PointCloudExportFormat.Ply,
        "pts" => PointCloudExportFormat.Pts,
        "xyz" => PointCloudExportFormat.Xyz,
        "dxf" => PointCloudExportFormat.Dxf,
        "las" => PointCloudExportFormat.Las,
        "geo-las" => PointCloudExportFormat.GeoLas,
        "laz" => PointCloudExportFormat.Laz,
        "copc" => PointCloudExportFormat.Copc,
        "e57" => PointCloudExportFormat.E57,
        _ => throw new EchoException("Unknown point-cloud format. See export cloud --help.", 2)
    };
}
