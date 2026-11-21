using System.Buffers.Binary;
using Echo.Geometry;
using Echo.Storage.Records;
using Echo.Storage;

namespace Echo.Simplification;

internal sealed class AdjacencyHeadCodec : RecordCodec<AdjacencyHead>
{
    public override int Size => 16;
    public override AdjacencyHead Decode(ReadOnlySpan<byte> bytes) => new(BinaryPrimitives.ReadInt32LittleEndian(bytes[0..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]));
    public override void Encode(AdjacencyHead value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes[0..], value.First);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], value.Last);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], value.Free);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[12..], value.Count);
    }
}
