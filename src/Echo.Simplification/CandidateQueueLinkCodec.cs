using System.Buffers.Binary;
using Echo.Storage;

namespace Echo.Simplification;

internal sealed class CandidateQueueLinkCodec : RecordCodec<CandidateQueueLink>
{
    public override int Size => 16;

    public override CandidateQueueLink Decode(ReadOnlySpan<byte> bytes)
        => new(BinaryPrimitives.ReadInt32LittleEndian(bytes), BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]));

    public override void Encode(CandidateQueueLink value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value.A);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], value.B);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], value.Next);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[12..], value.Slot);
    }
}
