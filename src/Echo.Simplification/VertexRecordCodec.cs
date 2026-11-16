using System.Buffers.Binary;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Simplification;

internal sealed class VertexRecordCodec : RecordCodec<Vertex>
{
    private readonly VertexFacesStore faces;

    public VertexRecordCodec(VertexFacesStore faces)
    {
        this.faces = faces;
    }

    public override int Size => 165;

    public override Vertex Decode(ReadOnlySpan<byte> bytes)
    {
        var id = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        var vertex = new Vertex(new VertexFaces(faces, id))
        {
            Id = id,
            Position = new V3(BinaryPrimitives.ReadDoubleLittleEndian(bytes[4..]), BinaryPrimitives.ReadDoubleLittleEndian(bytes[12..]), BinaryPrimitives.ReadDoubleLittleEndian(bytes[20..])),
            PlaneCount = BinaryPrimitives.ReadInt32LittleEndian(bytes[156..]),
            Revision = BinaryPrimitives.ReadInt32LittleEndian(bytes[160..]),
            Removed = bytes[164] != 0
        };
        for (var index = 0; index < 16; index++)
        {
            vertex.Quadric[index] = BinaryPrimitives.ReadDoubleLittleEndian(bytes[(28 + index * 8)..]);
        }
        return vertex;
    }

    public override void Encode(Vertex value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value.Id);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[4..], value.Position.X);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[12..], value.Position.Y);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[20..], value.Position.Z);
        for (var index = 0; index < 16; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes[(28 + index * 8)..], value.Quadric[index]);
        }
        BinaryPrimitives.WriteInt32LittleEndian(bytes[156..], value.PlaneCount);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[160..], value.Revision);
        bytes[164] = value.Removed ? (byte)1 : (byte)0;
    }
}
