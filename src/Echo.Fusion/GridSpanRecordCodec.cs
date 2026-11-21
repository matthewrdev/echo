using System.Buffers.Binary;
using Echo.Storage.Records;
using Echo.Storage;

namespace Echo.Fusion;

public sealed class GridSpanRecordCodec : RecordCodec<GridSpan>
{
    public override int Size => 16;

    public override GridSpan Decode(ReadOnlySpan<byte> bytes) => new(
        BinaryPrimitives.ReadInt32LittleEndian(bytes), BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]),
        BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]));

    public override void Encode(GridSpan value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value.X);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], value.Y);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], value.FirstZ);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[12..], value.LastZ);
    }
}
