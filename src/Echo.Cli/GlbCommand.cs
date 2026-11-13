using System.ComponentModel.Composition;
using Echo.Export;

namespace Echo.Cli;

[Export(typeof(ICliCommand)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class GlbCommand : ICliCommand
{
    private readonly IGlbWriter glbWriter;
    private readonly ICommandOutput output;

    [ImportingConstructor]
    public GlbCommand(IGlbWriter glbWriter, ICommandOutput output)
    {
        this.glbWriter = glbWriter;
        this.output = output;
    }

    public IReadOnlyCollection<string> Names => ["inspect-glb", "compare-glb"];

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
