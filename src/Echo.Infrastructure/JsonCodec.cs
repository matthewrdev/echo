using System.ComponentModel.Composition;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Echo.Storage;
using Echo.Contracts;

namespace Echo.Infrastructure;

[Export(typeof(IJsonCodec)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class JsonCodec : IJsonCodec
{
    private readonly IBulkArtifactStore bulk;
    private readonly JsonSerializerOptions compactOptions;

    [ImportingConstructor]
    public JsonCodec([ImportMany] IEnumerable<IJsonReadMetadata> metadata, IBulkArtifactStore bulk)
    {
        this.bulk = bulk;
        Options.TypeInfoResolver = JsonTypeInfoResolver.Combine(metadata.ToArray());
        Options.MakeReadOnly();
        compactOptions = new JsonSerializerOptions(Options) { WriteIndented = false };
        compactOptions.MakeReadOnly();
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
            new JsonStringEnumConverter<PerformanceBackend>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            new JsonStringEnumConverter<PerformanceStatus>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            new JsonStringEnumConverter<PerformanceSpanKind>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            new JsonStringEnumConverter<ComputeBackend>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            new JsonStringEnumConverter<IslandTrimMode>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            new JsonStringEnumConverter<ProcessingMemoryMode>(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
        }
    };

    public string Encode<T>(T value, bool compact = false)
    {
        var options = compact ? compactOptions : Options;
        return JsonSerializer.Serialize(value, options.GetTypeInfo(value?.GetType() ?? typeof(T)));
    }

    public void Write<T>(Stream stream, T value)
    {
        JsonSerializer.Serialize(stream, value, Options.GetTypeInfo(value?.GetType() ?? typeof(T)));
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
        if (Options.GetTypeInfo(typeof(T)) is JsonTypeInfo<T> info)
        {
            return info;
        }

        throw new EchoException($"No generated JSON read contract for {typeof(T).FullName}.");
    }
}
