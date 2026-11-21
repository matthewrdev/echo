using System.Buffers.Binary;
using Echo.Geometry;
using Echo.Storage.Records;
using Echo.Storage;

namespace Echo.Simplification;

internal sealed class CandidateRecordCodec : RecordCodec<Candidate>
{
    public override int Size => 48;
    public override Candidate Decode(ReadOnlySpan<byte> bytes) => new(BinaryPrimitives.ReadInt32LittleEndian(bytes[0..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]), new V3(BinaryPrimitives.ReadDoubleLittleEndian(bytes[16..]), BinaryPrimitives.ReadDoubleLittleEndian(bytes[24..]), BinaryPrimitives.ReadDoubleLittleEndian(bytes[32..])), BinaryPrimitives.ReadDoubleLittleEndian(bytes[40..]));
    public override void Encode(Candidate value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes[0..], value.A);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], value.B);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], value.RevisionA);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[12..], value.RevisionB);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[16..], value.Position.X);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[24..], value.Position.Y);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[32..], value.Position.Z);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[40..], value.Error);
    }
}
