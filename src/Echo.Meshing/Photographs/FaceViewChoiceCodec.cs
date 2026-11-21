using System.Buffers.Binary;
using Echo.Storage.Records;
using Echo.Storage;

namespace Echo.Meshing.Photographs;

internal sealed class FaceViewChoiceCodec : RecordCodec<FaceViewChoice>
{
    public override int Size => 8;
    public override FaceViewChoice Decode(ReadOnlySpan<byte> bytes) => new(BinaryPrimitives.ReadInt32LittleEndian(bytes), BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]));
    public override void Encode(FaceViewChoice value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value.Face);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], value.Option);
    }
}
