using System.Text.Json.Serialization.Metadata;
using System.Text.Json.Serialization;
using System.Text.Json;
using Echo.Models;
using Echo.Storage.Artifacts;
using Echo.Storage;

namespace Echo.Infrastructure;

public sealed class JsonCodec : IJsonCodec
{
    private readonly IReadOnlyList<IJsonReadMetadata> metadata;
    private readonly IBulkArtifactStore bulk;

    public JsonCodec(IEnumerable<IJsonReadMetadata> metadata, IBulkArtifactStore bulk)
    {
        this.metadata = metadata.ToArray();
        this.bulk = bulk;
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

    public string Encode<T>(T value)
    {
        var info = FindType(value?.GetType() ?? typeof(T));
        return info != null ? JsonSerializer.Serialize(value, info) : JsonSerializer.Serialize(value, Options);
    }

    public void Write<T>(Stream stream, T value)
    {
        var info = FindType(value?.GetType() ?? typeof(T));
        if (info != null)
        {
            JsonSerializer.Serialize(stream, value, info);
        }
        else
        {
            // Anonymous command/event payloads have no persisted domain contract.
            // Fingerprinting retains its existing canonical serializer separately.
            JsonSerializer.Serialize(stream, value, Options);
        }
    }

    private JsonTypeInfo? FindType(Type type)
    {
        foreach (var source in metadata)
        {
            var info = source.GetTypeInfo(type);
            if (info != null)
            {
                return info;
            }
        }

        return null;
    }

    public T Read<T>(string path)
    {
        if (bulk.Supports(typeof(T)))
        {
            return bulk.Read<T>(path);
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize(stream, ReadType<T>()) ?? throw new EchoException($"Empty JSON: {path}");
    }

    public T Clone<T>(T value) => JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(value, ReadType<T>()), ReadType<T>())!;

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
