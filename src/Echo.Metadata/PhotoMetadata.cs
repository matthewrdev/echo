using System.Text.Json;

namespace Echo.Metadata;

public sealed class PhotoMetadata
{
    public Dictionary<string, JsonElement> Fields { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, byte[]> Payloads { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string group, string name, object? value)
    {
        var key = group + ":" + name;
        if (value is byte[] bytes)
        {
            Payloads[key] = bytes;
            Fields[key] = JsonSerializer.SerializeToElement($"(Binary data {bytes.Length} bytes)", MetadataJsonContext.Default.String);
        }
        else
        {
            var type = value?.GetType() ?? typeof(object);
            var contract = MetadataJsonContext.Default.GetTypeInfo(type)
                ?? throw new InvalidDataException($"Unsupported photo metadata value: {type.FullName}.");
            Fields[key] = JsonSerializer.SerializeToElement(value, contract);
        }
    }
}
