using System.Buffers.Binary;
using Echo.Storage;

namespace Echo.Meshing;

internal sealed class FaceViewCandidateCodec : RecordCodec<FaceViewCandidate>
{
    public override int Size => 20;

    public override FaceViewCandidate Decode(ReadOnlySpan<byte> bytes) => new(BinaryPrimitives.ReadInt32LittleEndian(bytes),
        BinaryPrimitives.ReadDoubleLittleEndian(bytes[4..]), BinaryPrimitives.ReadDoubleLittleEndian(bytes[12..]));

    public override void Encode(FaceViewCandidate value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value.View);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[4..], value.Cost);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[12..], value.Density);
    }
}
