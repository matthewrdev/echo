using System.Text.Json;
using Echo.Infrastructure;

namespace Echo.Cli;

public sealed class CommandOutput : ICommandOutput
{
    private readonly IJsonCodec jsonCodec;

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
        var options = new JsonSerializerOptions(jsonCodec.Options)
        {
            WriteIndented = false
        };
        var line = JsonSerializer.Serialize(value, options);
        var writer = ndjson ? Console.Out : Console.Error;
        writer.WriteLine(line);
    }

    public void Error(string message, int exitCode)
    {
        var options = new JsonSerializerOptions(jsonCodec.Options)
        {
            WriteIndented = false
        };
        Console.Error.WriteLine(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            error = message,
            exitCode
        }, options));
    }
}
