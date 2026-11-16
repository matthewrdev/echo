using System.Buffers.Binary;
using Echo.Storage;

namespace Echo.Meshing;

internal sealed class SurfaceTriangleVerticesCodec : RecordCodec<SurfaceTriangleVertices>
{
    public override int Size => 12;

    public override SurfaceTriangleVertices Decode(ReadOnlySpan<byte> bytes) => new(
        BinaryPrimitives.ReadInt32LittleEndian(bytes),
        BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]),
        BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]));

    public override void Encode(SurfaceTriangleVertices value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value.A);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], value.B);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], value.C);
    }
}
