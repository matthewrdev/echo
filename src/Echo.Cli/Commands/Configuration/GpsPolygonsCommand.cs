using Echo.Infrastructure;
using Echo.Models;
using Echo.Projects;

namespace Echo.Cli.Commands.Configuration;

public sealed class GpsPolygonsCommand : ICliCommand
{
    private readonly IProjectStoreFactory storeFactory;
    private readonly ICommandOutput output;
    private readonly IJsonCodec jsonCodec;

    public GpsPolygonsCommand(IProjectStoreFactory storeFactory, ICommandOutput output, IJsonCodec jsonCodec)
    {
        this.storeFactory = storeFactory;
        this.output = output;
        this.jsonCodec = jsonCodec;
    }

    public IReadOnlyCollection<string> Names => ["gps-polygons"];

    public IReadOnlyList<CommandUsage> Usage =>
    [
        new("gps-polygons list",
            "--project PATH gps-polygons list",
            "List the project's GPS crop polygons."),
        new("gps-polygons import",
            "--project PATH gps-polygons import FILE",
            "Replace the polygons with a JSON array of objects with id, mode (include or exclude) and vertices (latitude and longitude in degrees). Use [] to clear all polygons.")
    ];

    public int Execute(Arguments args)
    {
        var store = storeFactory.Open(args.Get("project", null) ?? throw new EchoException("--project PATH is required.", 2));
        switch (args.Word(1, ""))
        {
            case "list":
                output.Write(store.Load().Settings.GpsPolygons ?? []);
                return 0;
            case "import":
                var polygons = jsonCodec.Read<List<GpsPolygon>>(args.RequiredWord(2));
                GpsPolygonValidation.ValidateAll(polygons);
                var project = store.Edit(data => data.Settings.GpsPolygons = polygons.Count == 0 ? null : polygons);
                output.Write(new
                {
                    project.Id,
                    project.Revision
                });
                return 0;
            default:
                throw new EchoException("Unknown gps-polygons action. Use --help.", 2);
        }
    }
}
