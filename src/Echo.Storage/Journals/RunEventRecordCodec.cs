using Echo.Models;

namespace Echo.Storage.Journals;

public sealed class RunEventRecordCodec : IBinaryJournalCodec<RunEvent>
{
    public uint Magic => 0x31564545; // EEV1

    public RunEvent Read(BinaryReader reader) => new(reader.ReadInt64(), new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero),
        reader.ReadString(), reader.ReadString(), ReadMap(reader, 0));

    public void Write(BinaryWriter writer, RunEvent value)
    {
        writer.Write(value.Sequence);
        writer.Write(value.Timestamp.UtcTicks);
        writer.Write(value.RunId);
        writer.Write(value.Type);
        WriteMap(writer, value.Payload);
    }

    private Dictionary<string, object?> ReadMap(BinaryReader reader, int depth)
    {
        var count = reader.ReadCount();
        var result = new Dictionary<string, object?>(count, StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            result.Add(reader.ReadString(), ReadValue(reader, depth + 1));
        }

        return result;
    }

    private object?[] ReadArray(BinaryReader reader, int depth)
    {
        var values = new object?[reader.ReadCount()];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = ReadValue(reader, depth + 1);
        }

        return values;
    }

    private object? ReadValue(BinaryReader reader, int depth)
    {
        if (depth >= 64)
        {
            throw new InvalidDataException("Event payload nesting is too deep.");
        }

        return reader.ReadByte() switch
        {
            0 => null,
            1 => reader.ReadString(),
            2 => reader.ReadInt64(),
            3 => reader.ReadDouble(),
            4 => false,
            5 => true,
            6 => ReadMap(reader, depth),
            7 => ReadArray(reader, depth),
            8 => reader.ReadUInt64(),
            _ => throw new InvalidDataException("Unknown event value.")
        };
    }

    private void WriteMap(BinaryWriter writer, IReadOnlyDictionary<string, object?> values)
    {
        writer.Write(values.Count);
        foreach (var pair in values)
        {
            writer.Write(pair.Key);
            WriteValue(writer, pair.Value);
        }
    }

    private void WriteValue(BinaryWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.Write((byte)0);
                break;
            case string text:
                writer.Write((byte)1);
                writer.Write(text);
                break;
            case int integer:
                writer.Write((byte)2);
                writer.Write((long)integer);
                break;
            case long integer:
                writer.Write((byte)2);
                writer.Write(integer);
                break;
            case ulong unsigned:
                writer.Write((byte)8);
                writer.Write(unsigned);
                break;
            case double number:
                writer.Write((byte)3);
                writer.Write(number);
                break;
            case bool boolean:
                writer.Write(boolean ? (byte)5 : (byte)4);
                break;
            case IReadOnlyDictionary<string, object?> map:
                writer.Write((byte)6);
                WriteMap(writer, map);
                break;
            case IReadOnlyList<object?> items:
                writer.Write((byte)7);
                writer.Write(items.Count);
                foreach (var item in items)
                {
                    WriteValue(writer, item);
                }
                break;
            default:
                throw new InvalidDataException($"Unsupported run event value {value.GetType().Name}.");
        }
    }
}
