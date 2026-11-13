using System.ComponentModel.Composition;
using System.Text.Json;
using System.Text.Json.Serialization;
using Echo.Contracts;

namespace Echo.Infrastructure;

[Export(typeof(IJsonCodec)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class JsonCodec : IJsonCodec
{
    [ImportingConstructor]
    public JsonCodec()
    {
    }

    public JsonSerializerOptions Options
    {
        get;
    } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    public string Encode<T>(T value) => JsonSerializer.Serialize(value, Options);

    public T Read<T>(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, Options) ?? throw new EchoException($"Empty JSON: {path}");
    }

    public T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value, Options), Options)!;
}
