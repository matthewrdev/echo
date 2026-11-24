using Echo.Models;
using Echo.Rendering;

namespace Echo.Cli.Commands.Images;

internal sealed class DroneScanCommand : ICliCommand
{
    private readonly INativeToolProcess native;
    private readonly ICommandOutput output;

    public DroneScanCommand(INativeToolProcess native, ICommandOutput output)
    {
        this.native = native;
        this.output = output;
    }

    public IReadOnlyCollection<string> Names => ["drone"];

    public IReadOnlyList<CommandUsage> Usage =>
    [
        new("drone scan",
            "drone scan --source CARD_ROOT",
            "Scan a drone card or copied card directory on macOS. Return site groups with photo import manifests.")
    ];

    public int Execute(Arguments arguments)
    {
        var invalidArguments = arguments.Words.Count != 2 || arguments.Word(1, "") != "scan"
            || arguments.Options.Keys.Any(key => key is not ("source" or "json"));
        if (invalidArguments)
        {
            throw new EchoException("Use drone scan --source CARD_ROOT.", 2);
        }

        var source = arguments.Get("source", null) ?? throw new EchoException("--source CARD_ROOT is required.", 2);
        var root = Path.GetFullPath(source);
        if (!Directory.Exists(root))
        {
            throw new EchoException("The drone card directory does not exist.", 2);
        }

        var context = DroneScanJsonContext.Default;
        var groups = native.Invoke("drone-scan", new DroneScanRequest(root), context.DroneScanRequest, context.DroneScanGroupArray);
        output.Write(new
        {
            schemaVersion = 1,
            source = root,
            groups
        });
        return 0;
    }
}
