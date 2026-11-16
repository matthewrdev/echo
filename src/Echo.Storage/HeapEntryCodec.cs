using System.Buffers.Binary;

namespace Echo.Storage;

public sealed class HeapEntryCodec<T> : RecordCodec<HeapEntry<T>>
{
    private readonly RecordCodec<T> valueCodec;

    public HeapEntryCodec(RecordCodec<T> valueCodec)
    {
        this.valueCodec = valueCodec;
    }

    public override int Size => valueCodec.Size + 8;
    public override HeapEntry<T> Decode(ReadOnlySpan<byte> bytes)
        => new(valueCodec.Decode(bytes[..valueCodec.Size]), BinaryPrimitives.ReadDoubleLittleEndian(bytes[valueCodec.Size..]));
    public override void Encode(HeapEntry<T> value, Span<byte> bytes)
    {
        valueCodec.Encode(value.Value, bytes[..valueCodec.Size]);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[valueCodec.Size..], value.Priority);
    }
}
