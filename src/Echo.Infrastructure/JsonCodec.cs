using System.ComponentModel.Composition;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Echo.Contracts;

namespace Echo.Infrastructure;

[Export(typeof(IJsonCodec)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class JsonCodec : IJsonCodec
{
    private readonly IReadOnlyList<IJsonReadMetadata> metadata;

    [ImportingConstructor]
    public JsonCodec([ImportMany] IEnumerable<IJsonReadMetadata> metadata)
    {
        this.metadata = metadata.ToArray();
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
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
        }
    };

    public string Encode<T>(T value) => JsonSerializer.Serialize(value, Options);

    public T Read<T>(string path)
    {
        if (typeof(T) == typeof(DepthMap))
        {
            return (T)(object)DepthMapJsonReader.Read(File.ReadAllBytes(path));
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize(stream, ReadType<T>()) ?? throw new EchoException($"Empty JSON: {path}");
    }

    public T Clone<T>(T value) => JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(value, Options), ReadType<T>())!;

    private JsonTypeInfo<T> ReadType<T>()
    {
        foreach (var source in metadata)
        {
            if (source.GetTypeInfo(typeof(T)) is JsonTypeInfo<T> info)
            {
                return info;
            }
        }

        throw new EchoException($"No generated JSON read contract for {typeof(T).FullName}.");
    }
}
