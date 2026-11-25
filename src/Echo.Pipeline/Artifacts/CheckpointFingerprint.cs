using System.Collections;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Echo.Models;

namespace Echo.Pipeline.Artifacts;

internal sealed class CheckpointFingerprint
{
    private readonly CheckpointJsonContext context;

    public CheckpointFingerprint()
    {
        context = new CheckpointJsonContext(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters =
            {
                new JsonStringEnumConverter<ComputeBackend>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
                new JsonStringEnumConverter<IslandTrimMode>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
                new JsonStringEnumConverter<SubjectDistanceMode>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
                new JsonStringEnumConverter<ExclusionShape>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
                new JsonStringEnumConverter<CropRegionMode>(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
            }
        });
    }

    public string Hash(string stage, string version, object inputs)
    {
        using var hash = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteNumber("storageVersion", 3);
            json.WriteString("version", version);
            json.WriteString("stage", stage);
            json.WritePropertyName("inputs");
            Write(json, inputs);
            json.WriteEndObject();
        }

        stream.FlushFinalBlock();
        return Convert.ToHexStringLower(hash.Hash!);
    }

    private void Write(Utf8JsonWriter json, object? value)
    {
        switch (value)
        {
            case null:
                json.WriteNullValue();
                break;
            case string text:
                json.WriteStringValue(text);
                break;
            case bool boolean:
                json.WriteBooleanValue(boolean);
                break;
            case int integer:
                json.WriteNumberValue(integer);
                break;
            case long wideInteger:
                json.WriteNumberValue(wideInteger);
                break;
            case double number:
                json.WriteNumberValue(number);
                break;
            case float single:
                json.WriteNumberValue(single);
                break;
            case IReadOnlyDictionary<string, object?> fields:
                WriteFields(json, fields);
                break;
            case IReadOnlyDictionary<string, string> index:
                json.WriteStartObject();
                foreach (var field in index)
                {
                    json.WriteString(field.Key, field.Value);
                }

                json.WriteEndObject();
                break;
            case IEnumerable items:
                json.WriteStartArray();
                foreach (var item in items)
                {
                    Write(json, item);
                }

                json.WriteEndArray();
                break;
            default:
                var type = context.GetTypeInfo(value.GetType())
                    ?? throw new InvalidOperationException($"No checkpoint fingerprint contract for {value.GetType().Name}.");
                JsonSerializer.Serialize(json, value, type);
                break;
        }
    }

    private void WriteFields(Utf8JsonWriter json, IReadOnlyDictionary<string, object?> fields)
    {
        json.WriteStartObject();
        foreach (var field in fields)
        {
            json.WritePropertyName(field.Key);
            Write(json, field.Value);
        }

        json.WriteEndObject();
    }
}
