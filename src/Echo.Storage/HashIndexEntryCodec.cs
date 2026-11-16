using System.Buffers.Binary;

namespace Echo.Storage;

internal sealed class HashIndexEntryCodec<TKey, TValue> : RecordCodec<HashIndexEntry<TKey, TValue>>
{
    private readonly RecordCodec<TKey> keys;
    private readonly RecordCodec<TValue> values;

    public HashIndexEntryCodec(RecordCodec<TKey> keys, RecordCodec<TValue> values)
    {
        this.keys = keys;
        this.values = values;
    }

    public override int Size => checked(keys.Size + values.Size + sizeof(int));

    public override HashIndexEntry<TKey, TValue> Decode(ReadOnlySpan<byte> bytes)
        => new(keys.Decode(bytes[..keys.Size]), values.Decode(bytes.Slice(keys.Size, values.Size)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[(keys.Size + values.Size)..]));

    public override void Encode(HashIndexEntry<TKey, TValue> value, Span<byte> bytes)
    {
        keys.Encode(value.Key, bytes[..keys.Size]);
        values.Encode(value.Value, bytes.Slice(keys.Size, values.Size));
        BinaryPrimitives.WriteInt32LittleEndian(bytes[(keys.Size + values.Size)..], value.Next);
    }
}
