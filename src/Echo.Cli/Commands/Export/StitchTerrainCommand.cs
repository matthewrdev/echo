using Echo.Models;
using Echo.Pipeline.Exporting;

namespace Echo.Cli.Commands.Export;

internal sealed class StitchTerrainCommand : ICliCommand
{
    private readonly ITerrainStitchService stitch;
    private readonly ICommandOutput output;

    public StitchTerrainCommand(ITerrainStitchService stitch, ICommandOutput output)
    {
        this.stitch = stitch;
        this.output = output;
    }

    public IReadOnlyCollection<string> Names => ["stitch-terrain"];

    public IReadOnlyList<CommandUsage> Usage =>
    [
        new("stitch-terrain", "stitch-terrain MANIFEST --output NEW_DIRECTORY",
            "Build terrain from elevation and imagery, with optional registered photo overlays, a footprint and textured Echo wall runs.")
    ];

    public int Execute(Arguments arguments)
    {
        if (arguments.Words.Count != 2 || arguments.Options.Keys.Any(key => key is not ("output" or "json")))
        {
            throw new EchoException("Use stitch-terrain MANIFEST --output NEW_DIRECTORY.", 2);
        }

        var manifest = arguments.RequiredWord(1);
        var directory = Path.GetFullPath(arguments.Get("output", null) ?? throw new EchoException("--output NEW_DIRECTORY is required.", 2));
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, args) =>
        {
            args.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            stitch.Stitch(manifest, directory, cancellation.Token);
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }

        output.Write(new
        {
            directory,
            model = "cliff.glb",
            georeference = "cliff.geo.json",
            seams = "terrain-stitch.json"
        });
        return 0;
    }
}
