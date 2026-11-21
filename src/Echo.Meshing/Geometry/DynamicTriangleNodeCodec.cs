using System.Buffers.Binary;
using Echo.Geometry;
using Echo.Storage.Records;
using Echo.Storage;

namespace Echo.Meshing.Geometry;

internal sealed class DynamicTriangleNodeCodec : RecordCodec<DynamicTriangleNode>
{
    public override int Size => 132;

    public override DynamicTriangleNode Decode(ReadOnlySpan<byte> bytes) => new(
        new SurfaceTriangle(Read(bytes), Read(bytes[24..]), Read(bytes[48..])), Read(bytes[72..]), Read(bytes[96..]),
        BinaryPrimitives.ReadInt32LittleEndian(bytes[120..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[124..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[128..]));

    public override void Encode(DynamicTriangleNode value, Span<byte> bytes)
    {
        Write(value.Triangle.A, bytes);
        Write(value.Triangle.B, bytes[24..]);
        Write(value.Triangle.C, bytes[48..]);
        Write(value.Minimum, bytes[72..]);
        Write(value.Maximum, bytes[96..]);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[120..], value.Left);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[124..], value.Right);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[128..], value.Height);
    }

    private V3 Read(ReadOnlySpan<byte> bytes) => new(BinaryPrimitives.ReadDoubleLittleEndian(bytes),
        BinaryPrimitives.ReadDoubleLittleEndian(bytes[8..]), BinaryPrimitives.ReadDoubleLittleEndian(bytes[16..]));

    private void Write(V3 value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteDoubleLittleEndian(bytes, value.X);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[8..], value.Y);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[16..], value.Z);
    }
}
