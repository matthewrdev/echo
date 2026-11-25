using Echo.Export;
using Echo.Models;

namespace Echo.Cli.Commands.Export;

public sealed class GlbCommand : ICliCommand
{
    private readonly IGlbWriter glbWriter;
    private readonly IGlbCompressor glbCompressor;
    private readonly ICommandOutput output;

    public GlbCommand(IGlbWriter glbWriter, IGlbCompressor glbCompressor, ICommandOutput output)
    {
        this.glbWriter = glbWriter;
        this.glbCompressor = glbCompressor;
        this.output = output;
    }

    public IReadOnlyCollection<string> Names => ["inspect-glb", "compare-glb", "compress-glb"];

    public IReadOnlyList<CommandUsage> Usage =>
    [
        new("inspect-glb",
            "inspect-glb FILE",
            "Read mesh and texture properties from a GLB."),
        new("compare-glb",
            "compare-glb FILE REFERENCE",
            "Compare exported model properties against a reference GLB."),
        new("compress-glb",
            "compress-glb ECHO_GLB OUTPUT [--jpeg-quality QUALITY]",
            "Write a Draco GLB from an Echo export after verifying decoded UV triangle counts."),
    ];

    public int Execute(Arguments arguments)
    {
        if (arguments.Words[0] == "compress-glb")
        {
            return Compress(arguments);
        }

        var inspection = glbWriter.Inspect(arguments.RequiredWord(1));
        if (arguments.Words[0] == "inspect-glb")
        {
            output.Write(inspection);
        }
        else
        {
            output.Write(new
            {
                output = inspection,
                reference = glbWriter.Inspect(arguments.RequiredWord(2)),
                comparison = "Structural summary only; coordinate frames have not been registered."
            });
        }

        return 0;
    }

    private int Compress(Arguments arguments)
    {
        var invalidArguments = arguments.Words.Count != 3
            || arguments.Options.Keys.Any(name => name is not ("jpeg-quality" or "json"));
        if (invalidArguments)
        {
            throw new EchoException("Use compress-glb ECHO_GLB OUTPUT [--jpeg-quality QUALITY].", 2);
        }

        int? quality = null;
        if (arguments.Options.ContainsKey("jpeg-quality"))
        {
            var value = arguments.Int("jpeg-quality", 101);
            if (value > 100)
            {
                throw new EchoException("--jpeg-quality must be between 1 and 100.", 2);
            }

            quality = value;
        }

        var source = arguments.RequiredWord(1);
        var destination = arguments.RequiredWord(2);
        var uvBits = glbCompressor.Compress(source, destination, quality);
        output.Write(new
        {
            path = Path.GetFullPath(destination),
            uvBits,
            bytes = new FileInfo(destination).Length
        });
        return 0;
    }
}
