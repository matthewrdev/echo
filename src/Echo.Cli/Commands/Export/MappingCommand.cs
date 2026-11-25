using System.Globalization;
using Echo.Cli.Commands.Geography;
using Echo.Export;
using Echo.Geometry;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Pipeline.Exporting;
using Echo.Projects;
using Echo.Rendering;

namespace Echo.Cli.Commands.Export;

internal sealed class MappingCommand : ICliCommand
{
    private readonly IProjectStoreFactory stores;
    private readonly IJsonCodec json;
    private readonly IFileOperations files;
    private readonly IRunExchangeService exchange;
    private readonly IGeographicRunModelSource models;
    private readonly INativeToolProcess native;
    private readonly IMappingProductWriter mapping;
    private readonly ICommandOutput output;

    public MappingCommand(IProjectStoreFactory stores, IJsonCodec json, IFileOperations files,
        IRunExchangeService exchange, IGeographicRunModelSource models, INativeToolProcess native,
        IMappingProductWriter mapping, ICommandOutput output)
    {
        this.stores = stores;
        this.json = json;
        this.files = files;
        this.exchange = exchange;
        this.models = models;
        this.native = native;
        this.mapping = mapping;
        this.output = output;
    }

    public IReadOnlyCollection<string> Names => ["export-map"];

    public IReadOnlyList<CommandUsage> Usage =>
    [
        new("export-map",
            "--project PATH export-map --output NEW_DIRECTORY --crs EPSG:CODE --resolution METRES [--run RUN_ID] [--relative-heights]",
            "Export a mesh-derived orthophoto, DSM, DTM and classified LAZ from a saved textured run on macOS. Requires PDAL and GDAL. Use --relative-heights to explicitly export elevations relative to the run origin.")
    ];

    public int Execute(Arguments arguments)
    {
        var invalidArguments = arguments.Words.Count != 1
            || arguments.Options.Keys.Any(key => key is not ("project" or "run" or "output" or "crs" or "resolution" or "relative-heights" or "json"));
        if (invalidArguments)
        {
            throw new EchoException("Invalid mapping export arguments. Use export-map --help.", 2);
        }

        var project = arguments.Get("project", null) ?? throw new EchoException("--project PATH is required.", 2);
        var destination = arguments.Get("output", null) ?? throw new EchoException("--output NEW_DIRECTORY is required.", 2);
        var crs = ReadCrs(arguments.Get("crs", null));
        var resolution = arguments.Number("resolution");
        if (!double.IsFinite(resolution) || resolution < 0.001)
        {
            throw new EchoException("--resolution must be finite and at least 0.001 metres.", 2);
        }

        var store = stores.Open(project);
        store.Load();
        var run = store.GetRun(arguments.Get("run", "latest")!);
        var alignment = ReadAlignment(store, run);
        var origin = alignment.Origin ?? throw new EchoException("Mapping requires a geographically aligned run.", 2);
        var relativeHeights = arguments.Options.ContainsKey("relative-heights");
        if (!relativeHeights && !GeographicCoordinates.HasEllipsoidalHeight(origin))
        {
            throw new EchoException("Absolute mapping heights require a known WGS84 ellipsoidal origin. Use --relative-heights to explicitly select local heights.", 2);
        }

        var model = models.Resolve(store, run, origin);
        if (model == null || !run.Artifacts.ContainsKey("texture"))
        {
            throw new EchoException("Process the run through Texture before exporting a map.", 2);
        }

        destination = ValidateDestination(store, destination);
        mapping.ValidateCrs(crs);
        output.Write(WritePackage(store, run, model, alignment, origin, destination, crs, resolution, relativeHeights));
        return 0;
    }

    private ReconstructionOverview ReadAlignment(IProjectStore store, RunData run)
    {
        if (!run.Artifacts.TryGetValue("alignment", out var alignment))
        {
            throw new EchoException("A completed geographic alignment is required.", 2);
        }

        return json.Read<ReconstructionOverview>(store.PathFor(alignment + "/scene.json"));
    }

    private string ReadCrs(string? value)
    {
        var crs = value?.Trim().ToUpperInvariant() ?? "";
        var validCode = crs.StartsWith("EPSG:", StringComparison.Ordinal)
            && int.TryParse(crs.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var code) && code > 0;
        return validCode ? crs : throw new EchoException("Specify a projected --crs EPSG:CODE.", 2);
    }

    private string ValidateDestination(IProjectStore store, string path)
    {
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new EchoException("Choose a new export folder.", 2);
        }

        foreach (var folder in new[] { "artifacts", "sources", "runs", "work" })
        {
            var managed = Path.GetFullPath(store.PathFor(folder)) + Path.DirectorySeparatorChar;
            if ((destination + Path.DirectorySeparatorChar).StartsWith(managed, StringComparison.OrdinalIgnoreCase))
            {
                throw new EchoException("Choose an export folder outside the project's managed folders.", 2);
            }
        }

        return destination;
    }

    private RunExchangeResult WritePackage(IProjectStore store, RunData run, GeographicModelSource model,
        ReconstructionOverview alignment, GeoPosition origin, string destination, string crs, double resolution, bool relativeHeights)
    {
        var staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var work = Path.Combine(staging, "work");
        Directory.CreateDirectory(work);
        try
        {
            var cloud = Path.Combine(work, "cloud");
            exchange.ExportPointCloud(store, run.Id, cloud, PointCloudExportFormat.Las);
            var context = GeographicNativeJsonContext.Default;
            var rendered = native.Invoke("geographic-raster", new GeographicRasterRequest(model, work, 4096),
                context.GeographicRasterRequest, context.Boolean);
            if (!rendered)
            {
                throw new EchoException("The native helper did not render the mapping surface.", 5);
            }

            var mappingProducts = mapping.Write(Path.Combine(cloud, "cloud.las"), Path.Combine(work, "model-map.tif"),
                staging, origin, crs, resolution, relativeHeights);
            var registered = alignment.Cameras.Select(camera => camera.ImageId).ToHashSet(StringComparer.Ordinal);
            var enabled = run.Snapshot.Images.Where(photo => photo.Enabled).ToArray();
            var groups = enabled.SelectMany(photo => photo.Sets.Select(set => new
            {
                Set = set,
                Photo = photo
            })).GroupBy(item => item.Set).OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new
                {
                    name = group.Key,
                    inputPhotos = group.Count(),
                    registeredPhotos = group.Count(item => registered.Contains(item.Photo.Id)),
                    unregisteredPhotos = group.Where(item => !registered.Contains(item.Photo.Id)).Select(item => item.Photo.Name).ToArray()
                }).ToArray();
            var controlReport = store.PathFor(run.Artifacts["alignment"] + "/survey-control.json");
            if (File.Exists(controlReport))
            {
                File.Copy(controlReport, Path.Combine(staging, "survey-control.json"));
            }

            files.AtomicJson(Path.Combine(staging, "mapping.json"), new
            {
                schemaVersion = 1,
                runId = run.Id,
                horizontalCrs = crs,
                verticalReference = relativeHeights ? "Metres relative to the reconstruction origin" : "WGS84 ellipsoid",
                origin,
                grid = mappingProducts.Grid,
                mappingProducts.SourceOrthophotoPixelMetres,
                mappingProducts.OrthophotoAlphaCoveragePercent,
                mappingProducts.DsmCoveragePercent,
                mappingProducts.DtmCoveragePercent,
                elevationNoData = -9999,
                orthophotoSource = "Top-down textured mesh rendered at 4096 pixels before reprojection",
                groundClassifier = "PDAL SMRF",
                inputPhotos = enabled.Length,
                registeredPhotos = enabled.Count(photo => registered.Contains(photo.Id)),
                unregisteredPhotos = enabled.Where(photo => !registered.Contains(photo.Id)).Select(photo => photo.Name).ToArray(),
                imageSets = groups,
                checkPointReport = File.Exists(controlReport) ? "survey-control.json" : null
            });
            Directory.Delete(work, recursive: true);
            var products = Directory.GetFiles(staging).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToArray();
            Directory.Move(staging, destination);
            return new RunExchangeResult(run.Id, destination, products);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }
}
