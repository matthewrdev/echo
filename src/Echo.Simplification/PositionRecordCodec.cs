using System.Buffers.Binary;
using Echo.Geometry;
using Echo.Storage.Records;
using Echo.Storage;

namespace Echo.Simplification;

internal sealed class PositionRecordCodec : RecordCodec<V3>
{
    public override int Size => 24;

    public override V3 Decode(ReadOnlySpan<byte> bytes) => new(
        BinaryPrimitives.ReadDoubleLittleEndian(bytes),
        BinaryPrimitives.ReadDoubleLittleEndian(bytes[8..]),
        BinaryPrimitives.ReadDoubleLittleEndian(bytes[16..]));

    public override void Encode(V3 value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteDoubleLittleEndian(bytes, value.X);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[8..], value.Y);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[16..], value.Z);
    }
}
