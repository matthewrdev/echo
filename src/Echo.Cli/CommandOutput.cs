using System.Text.Json;
using Echo.Infrastructure;
using Echo.Models;

namespace Echo.Cli;

public sealed class CommandOutput : ICommandOutput
{
    private readonly IJsonCodec jsonCodec;
    private readonly JsonSerializerOptions compact;
    private readonly CoreJsonReadContext eventJson;

    public CommandOutput(IJsonCodec jsonCodec)
    {
        this.jsonCodec = jsonCodec;
        compact = new JsonSerializerOptions(jsonCodec.Options) { WriteIndented = false };
        eventJson = new CoreJsonReadContext(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    public void Write(object value)
    {
        Console.WriteLine(jsonCodec.Encode(value));
    }

    public void Progress(object value, bool ndjson)
    {
        var line = value switch
        {
            RunEvent record => JsonSerializer.Serialize(record, eventJson.RunEvent),
            PhotoImportProgress import => JsonSerializer.Serialize(import, eventJson.PhotoImportProgress),
            _ => JsonSerializer.Serialize(value, compact)
        };
        var writer = ndjson ? Console.Out : Console.Error;
        writer.WriteLine(line);
    }

    public void Error(string message, int exitCode)
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            error = message,
            exitCode
        }, compact));
    }
}
