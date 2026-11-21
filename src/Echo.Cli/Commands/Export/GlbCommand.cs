using Echo.Export;

namespace Echo.Cli.Commands.Export;

public sealed class GlbCommand : ICliCommand
{
    private readonly IGlbWriter glbWriter;
    private readonly ICommandOutput output;

    public GlbCommand(IGlbWriter glbWriter, ICommandOutput output)
    {
        this.glbWriter = glbWriter;
        this.output = output;
    }

    public IReadOnlyCollection<string> Names => ["inspect-glb", "compare-glb"];

    public IReadOnlyList<CommandUsage> Usage =>
    [
        new("inspect-glb",
            "inspect-glb FILE",
            "Read mesh and texture properties from a GLB."),
        new("compare-glb",
            "compare-glb FILE REFERENCE",
            "Compare exported model properties against a reference GLB."),
    ];

    public int Execute(Arguments arguments)
    {
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
}
