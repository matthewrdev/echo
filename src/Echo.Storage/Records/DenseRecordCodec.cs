using System.Buffers.Binary;
using Echo.Models;

namespace Echo.Storage.Records;

public sealed class DenseRecordCodec : RecordCodec<DenseSample>
{
    public override int Size => 23;
    public override DenseSample Decode(ReadOnlySpan<byte> bytes) => new DenseSample(BinaryPrimitives.ReadSingleLittleEndian(bytes), BinaryPrimitives.ReadSingleLittleEndian(bytes[4..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[8..]), bytes[12], bytes[13], bytes[14], BinaryPrimitives.ReadInt32LittleEndian(bytes[15..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[19..]));
    public override void Encode(DenseSample value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value.X);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[4..], value.Y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[8..], value.Z);
        bytes[12] = value.Red;
        bytes[13] = value.Green;
        bytes[14] = value.Blue;
        BinaryPrimitives.WriteInt32LittleEndian(bytes[15..], value.View);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[19..], value.Confidence);
    }
}
