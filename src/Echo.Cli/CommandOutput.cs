using System.ComponentModel.Composition;
using Echo.Infrastructure;

namespace Echo.Cli;

[Export(typeof(ICommandOutput)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class CommandOutput : ICommandOutput
{
    private readonly IJsonCodec jsonCodec;

    [ImportingConstructor]
    public CommandOutput(IJsonCodec jsonCodec)
    {
        this.jsonCodec = jsonCodec;
    }

    public void Write(object value)
    {
        Console.WriteLine(jsonCodec.Encode(value));
    }

    public void Progress(object value, bool ndjson)
    {
        var line = jsonCodec.Encode(value, compact: true);
        var writer = ndjson ? Console.Out : Console.Error;
        writer.WriteLine(line);
    }

    public void Error(string message, int exitCode)
    {
        Console.Error.WriteLine(jsonCodec.Encode(new CommandError
        {
            SchemaVersion = 1,
            Error = message,
            ExitCode = exitCode
        }, compact: true));
    }
}
