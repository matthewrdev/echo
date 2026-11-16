using System.Buffers.Binary;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Meshing;

internal sealed class SurfaceEdgeRecordCodec : RecordCodec<SurfaceEdge>
{
    public override int Size => 8;
    public override SurfaceEdge Decode(ReadOnlySpan<byte> bytes) => new(BinaryPrimitives.ReadInt32LittleEndian(bytes[0..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]));
    public override void Encode(SurfaceEdge value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes[0..], value.A);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], value.B);
    }
}
