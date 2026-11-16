using System.Buffers.Binary;
using Echo.Storage;

namespace Echo.Simplification;

internal sealed class IndexedCandidateCodec : RecordCodec<IndexedCandidate>
{
    private readonly CandidateRecordCodec candidates = new();

    public override int Size => 60;

    public override IndexedCandidate Decode(ReadOnlySpan<byte> bytes)
        => new(candidates.Decode(bytes), BinaryPrimitives.ReadDoubleLittleEndian(bytes[48..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[56..]));

    public override void Encode(IndexedCandidate value, Span<byte> bytes)
    {
        candidates.Encode(value.Value, bytes);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[48..], value.Priority);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[56..], value.Handle);
    }
}
