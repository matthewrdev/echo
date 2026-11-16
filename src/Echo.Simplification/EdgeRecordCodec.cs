using System.Buffers.Binary;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Simplification;

internal sealed class EdgeRecordCodec : RecordCodec<Edge>
{
    public override int Size => 8;
    public override Edge Decode(ReadOnlySpan<byte> bytes) => new(BinaryPrimitives.ReadInt32LittleEndian(bytes[0..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]));
    public override void Encode(Edge value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes[0..], value.A);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], value.B);
    }
}
