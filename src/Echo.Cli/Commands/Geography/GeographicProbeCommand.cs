using Echo.Infrastructure;
using Echo.Models;
using Echo.Projects;
using Echo.Rendering;

namespace Echo.Cli.Commands.Geography;

internal sealed class GeographicProbeCommand : ICliCommand
{
    private readonly IProjectStoreFactory stores;
    private readonly IJsonCodec json;
    private readonly IGeographicRunModelSource models;
    private readonly INativeToolProcess native;
    private readonly ICommandOutput output;

    public GeographicProbeCommand(IProjectStoreFactory stores, IJsonCodec json, IGeographicRunModelSource models,
        INativeToolProcess native, ICommandOutput output)
    {
        this.stores = stores;
        this.json = json;
        this.models = models;
        this.native = native;
        this.output = output;
    }

    public IReadOnlyCollection<string> Names => ["geo-probe"];

    public IReadOnlyList<CommandUsage> Usage =>
    [
        new("geo-probe",
            "--project PATH geo-probe --lat DEGREES --lon DEGREES [--alt METRES] [--accuracy METRES] [--run RUN_ID]",
            "Test a GPS coordinate against the saved model on macOS. Omit altitude to query the topmost surface intersection.")
    ];

    public int Execute(Arguments arguments)
    {
        var invalidArguments = arguments.Words.Count != 1
            || arguments.Options.Keys.Any(key => key is not ("project" or "lat" or "lon" or "alt" or "accuracy" or "run" or "json"));
        if (invalidArguments)
        {
            throw new EchoException("Invalid geographic probe arguments. Use geo-probe --help.", 2);
        }

        var root = arguments.Get("project", null) ?? throw new EchoException("--project PATH is required.", 2);
        var latitude = arguments.Get("lat", null) ?? throw new EchoException("--lat is required.", 2);
        var longitude = arguments.Get("lon", null) ?? throw new EchoException("--lon is required.", 2);
        var store = stores.Open(root);
        var run = store.GetRun(arguments.Get("run", "latest")!);
        if (!run.Artifacts.TryGetValue("alignment", out var alignment))
        {
            throw new EchoException("A completed geographic alignment is required.", 2);
        }

        var scene = json.Read<ReconstructionOverview>(store.PathFor(alignment + "/scene.json")).ToScene();
        if (scene.Origin == null || scene.Frame.Contains("arbitrary", StringComparison.OrdinalIgnoreCase))
        {
            throw new EchoException("This run has no metric geographic placement.", 2);
        }

        var model = models.Resolve(store, run, scene.Origin)
            ?? throw new EchoException("A saved mesh is required to test a GPS coordinate.", 2);
        var context = GeographicNativeJsonContext.Default;
        var request = new GeographicProbeRequest(model, latitude, longitude, arguments.Get("alt", null),
            arguments.Get("accuracy", "0")!);
        var result = native.Invoke("geographic-probe", request, context.GeographicProbeRequest, context.GeographicProbeResult);
        output.Write(result with
        {
            RunId = run.Id
        });
        return 0;
    }
}
