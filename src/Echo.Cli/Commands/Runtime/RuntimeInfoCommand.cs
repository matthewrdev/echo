
namespace Echo.Cli.Commands.Runtime;

public sealed class RuntimeInfoCommand : ICliCommand
{
    private readonly IRuntimeInfoProvider runtime;
    private readonly ICommandOutput output;

    public RuntimeInfoCommand(IRuntimeInfoProvider runtime, ICommandOutput output)
    {
        this.runtime = runtime;
        this.output = output;
    }

    public IReadOnlyCollection<string> Names => ["runtime-info"];

    public IReadOnlyList<CommandUsage> Usage =>
    [
        new("runtime-info",
            "runtime-info",
            "Read runtime, system and build information."),
    ];

    public int Execute(Arguments arguments)
    {
        output.Write(runtime.Read());
        return 0;
    }
}
