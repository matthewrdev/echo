using System.Buffers.Binary;
using Echo.Storage.Records;
using Echo.Storage;

namespace Echo.Simplification;

internal sealed class FaceVerticesCodec : RecordCodec<FaceVertices>
{
    public override int Size => 12;

    public override FaceVertices Decode(ReadOnlySpan<byte> bytes) => new(
        BinaryPrimitives.ReadInt32LittleEndian(bytes),
        BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]),
        BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]));

    public override void Encode(FaceVertices value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value.A);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], value.B);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], value.C);
    }
}
