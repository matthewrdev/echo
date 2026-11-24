using System.Text.Json;

namespace Echo.Storage.Journals;

public static class BinaryValues
{
    public static void WriteOptional<T>(this BinaryWriter writer, T? value, Action<T> write) where T : struct
    {
        writer.Write(value.HasValue);
        if (value.HasValue)
        {
            write(value.Value);
        }
    }

    public static T? ReadOptional<T>(this BinaryReader reader, Func<T> read) where T : struct
        => reader.ReadBoolean() ? read() : null;

    public static void WriteOptionalString(this BinaryWriter writer, string? value)
    {
        writer.Write(value != null);
        if (value != null)
        {
            writer.Write(value);
        }
    }

    public static string? ReadOptionalString(this BinaryReader reader)
        => reader.ReadBoolean() ? reader.ReadString() : null;

    public static int ReadCount(this BinaryReader reader)
    {
        var count = reader.ReadInt32();
        if (count < 0 || count > reader.BaseStream.Length - reader.BaseStream.Position)
        {
            throw new InvalidDataException("Invalid binary collection size.");
        }

        return count;
    }

    public static T[] ReadArray<T>(this BinaryReader reader, Func<T> read)
    {
        var values = new T[reader.ReadCount()];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = read();
        }

        return values;
    }

    public static void WriteCollection<T>(this BinaryWriter writer, IReadOnlyCollection<T> values, Action<T> write)
    {
        writer.Write(values.Count);
        foreach (var item in values)
        {
            write(item);
        }
    }

    public static void WriteJsonValue(this BinaryWriter writer, JsonElement value)
    {
        writer.Write((byte)value.ValueKind);
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = value.EnumerateObject().Count();
                writer.Write(properties);
                foreach (var property in value.EnumerateObject())
                {
                    writer.Write(property.Name);
                    writer.WriteJsonValue(property.Value);
                }
                break;
            case JsonValueKind.Array:
                writer.Write(value.GetArrayLength());
                foreach (var item in value.EnumerateArray())
                {
                    writer.WriteJsonValue(item);
                }
                break;
            case JsonValueKind.String:
                writer.Write(value.GetString()!);
                break;
            case JsonValueKind.Number:
                writer.Write(value.GetRawText());
                break;
        }
    }

    public static Dictionary<string, JsonElement> ReadJsonFields(this BinaryReader reader)
    {
        using var data = new MemoryStream();
        using (var writer = new Utf8JsonWriter(data))
        {
            writer.WriteStartObject();
            var count = reader.ReadCount();
            for (var index = 0; index < count; index++)
            {
                writer.WritePropertyName(reader.ReadString());
                ReadJsonValue(reader, writer, 0);
            }
            writer.WriteEndObject();
        }

        var fields = JsonElement.Parse(data.GetBuffer().AsSpan(0, checked((int)data.Length)));
        return fields.EnumerateObject().ToDictionary(property => property.Name, property => property.Value);
    }

    private static void ReadJsonValue(BinaryReader reader, Utf8JsonWriter writer, int depth)
    {
        if (depth >= 64)
        {
            throw new InvalidDataException("Binary value exceeds the maximum nesting depth.");
        }

        switch ((JsonValueKind)reader.ReadByte())
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var properties = reader.ReadCount();
                for (var index = 0; index < properties; index++)
                {
                    writer.WritePropertyName(reader.ReadString());
                    ReadJsonValue(reader, writer, depth + 1);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                var count = reader.ReadCount();
                for (var index = 0; index < count; index++)
                {
                    ReadJsonValue(reader, writer, depth + 1);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(reader.ReadString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(reader.ReadString());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidDataException("Unsupported binary value.");
        }
    }
}
