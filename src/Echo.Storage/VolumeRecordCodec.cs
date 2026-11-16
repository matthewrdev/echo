using System.Buffers.Binary;
using Echo.Contracts;

namespace Echo.Storage;

public sealed class VolumeRecordCodec : RecordCodec<VolumeSample>
{
    public override int Size => 20;
    public override VolumeSample Decode(ReadOnlySpan<byte> bytes) => new VolumeSample { X = BinaryPrimitives.ReadInt32LittleEndian(bytes), Y = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]), Z = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]), Distance = BinaryPrimitives.ReadSingleLittleEndian(bytes[12..]), Views = BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]) };
    public override void Encode(VolumeSample value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value.X);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], value.Y);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], value.Z);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[12..], value.Distance);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[16..], value.Views);
    }
}
