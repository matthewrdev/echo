using System.Buffers.Binary;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Simplification;

internal sealed class VertexRecordCodec : RecordCodec<Vertex>
{
    public override int Size => 117;

    public override Vertex Decode(ReadOnlySpan<byte> bytes)
    {
        var id = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        var vertex = new Vertex
        {
            Id = id,
            Position = new V3(BinaryPrimitives.ReadDoubleLittleEndian(bytes[4..]), BinaryPrimitives.ReadDoubleLittleEndian(bytes[12..]), BinaryPrimitives.ReadDoubleLittleEndian(bytes[20..])),
            PlaneCount = BinaryPrimitives.ReadInt32LittleEndian(bytes[108..]),
            Revision = BinaryPrimitives.ReadInt32LittleEndian(bytes[112..]),
            Removed = bytes[116] != 0
        };
        var offset = 28;
        for (var row = 0; row < 4; row++)
        {
            for (var column = row; column < 4; column++)
            {
                vertex[row * 4 + column] = BinaryPrimitives.ReadDoubleLittleEndian(bytes[offset..]);
                offset += sizeof(double);
            }
        }
        return vertex;
    }

    public override void Encode(Vertex value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value.Id);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[4..], value.Position.X);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[12..], value.Position.Y);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[20..], value.Position.Z);
        var offset = 28;
        for (var row = 0; row < 4; row++)
        {
            for (var column = row; column < 4; column++)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(bytes[offset..], value[row * 4 + column]);
                offset += sizeof(double);
            }
        }
        BinaryPrimitives.WriteInt32LittleEndian(bytes[108..], value.PlaneCount);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[112..], value.Revision);
        bytes[116] = value.Removed ? (byte)1 : (byte)0;
    }
}
