using System.Buffers.Binary;
using Echo.Storage;

namespace Echo.Geometry;

internal sealed class SurfaceTopologyEdgeCodec : RecordCodec<SurfaceTopologyEdge>
{
    public override int Size => 8;
    public override SurfaceTopologyEdge Decode(ReadOnlySpan<byte> bytes) => new(BinaryPrimitives.ReadInt32LittleEndian(bytes), BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]));
    public override void Encode(SurfaceTopologyEdge value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value.First);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], value.Second);
    }
}
