using System.Buffers.Binary;

namespace Echo.Storage.Records;

// Photograph IDs are shared by millions of faces. Keep only the small ID table
// in memory and page one integer per face, using ordinary IList<string> access.
public sealed class StringTableCodec : RecordCodec<string>
{
    private readonly List<string> strings;
    private readonly Dictionary<string, int> indices;

    public StringTableCodec(IEnumerable<string>? values = null)
    {
        strings = values?.ToList() ?? [];
        indices = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < strings.Count; index++)
        {
            indices.Add(strings[index], index);
        }
    }

    public override int Size => 4;

    public override string Decode(ReadOnlySpan<byte> bytes)
    {
        var index = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        if ((uint)index >= (uint)strings.Count)
        {
            throw new InvalidDataException("Invalid photograph index.");
        }

        return strings[index];
    }

    public override void Encode(string value, Span<byte> bytes)
    {
        if (!indices.TryGetValue(value, out var index))
        {
            index = strings.Count;
            indices.Add(value, index);
            strings.Add(value);
        }

        BinaryPrimitives.WriteInt32LittleEndian(bytes, index);
    }
}
