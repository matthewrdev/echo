using System.Buffers.Binary;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Meshing;

internal sealed class BoundaryEdgeRecordCodec : RecordCodec<BoundaryEdge>
{
    public override int Size => 36;
    public override BoundaryEdge Decode(ReadOnlySpan<byte> bytes) => new(BinaryPrimitives.ReadInt32LittleEndian(bytes[0..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]), new V3(BinaryPrimitives.ReadDoubleLittleEndian(bytes[8..]), BinaryPrimitives.ReadDoubleLittleEndian(bytes[16..]), BinaryPrimitives.ReadDoubleLittleEndian(bytes[24..])), BinaryPrimitives.ReadInt32LittleEndian(bytes[32..]));
    public override void Encode(BoundaryEdge value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes[0..], value.From);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], value.To);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[8..], value.Normal.X);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[16..], value.Normal.Y);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[24..], value.Normal.Z);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[32..], value.Count);
    }
}
