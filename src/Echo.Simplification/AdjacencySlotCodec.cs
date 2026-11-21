using System.Buffers.Binary;
using Echo.Geometry;
using Echo.Storage.Records;
using Echo.Storage;

namespace Echo.Simplification;

internal sealed class AdjacencySlotCodec : RecordCodec<AdjacencySlot>
{
    public override int Size => 16;
    public override AdjacencySlot Decode(ReadOnlySpan<byte> bytes) => new(BinaryPrimitives.ReadInt32LittleEndian(bytes[0..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]));
    public override void Encode(AdjacencySlot value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes[0..], value.Face);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], value.Next);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], value.FreeNext);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[12..], value.Previous);
    }
}
