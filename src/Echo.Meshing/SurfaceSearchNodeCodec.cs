using System.Buffers.Binary;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Meshing;

internal sealed class SurfaceSearchNodeCodec : RecordCodec<SurfaceSearchNode>
{
    public override int Size => 64;

    public override SurfaceSearchNode Decode(ReadOnlySpan<byte> bytes) => new(
        new V3(BinaryPrimitives.ReadDoubleLittleEndian(bytes), BinaryPrimitives.ReadDoubleLittleEndian(bytes[8..]), BinaryPrimitives.ReadDoubleLittleEndian(bytes[16..])),
        new V3(BinaryPrimitives.ReadDoubleLittleEndian(bytes[24..]), BinaryPrimitives.ReadDoubleLittleEndian(bytes[32..]), BinaryPrimitives.ReadDoubleLittleEndian(bytes[40..])),
        BinaryPrimitives.ReadInt32LittleEndian(bytes[48..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[52..]),
        BinaryPrimitives.ReadInt32LittleEndian(bytes[56..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[60..]));

    public override void Encode(SurfaceSearchNode value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteDoubleLittleEndian(bytes, value.Minimum.X);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[8..], value.Minimum.Y);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[16..], value.Minimum.Z);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[24..], value.Maximum.X);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[32..], value.Maximum.Y);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[40..], value.Maximum.Z);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[48..], value.Start);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[52..], value.Count);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[56..], value.Left);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[60..], value.Right);
    }
}
