using System.Buffers.Binary;
using Echo.Storage;

namespace Echo.Meshing;

internal sealed class VolumeGapSampleCodec : RecordCodec<VolumeGapSample>
{
    public override int Size => 16;

    public override VolumeGapSample Decode(ReadOnlySpan<byte> bytes) => new(
        new Grid(BinaryPrimitives.ReadInt32LittleEndian(bytes), BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[8..])), BinaryPrimitives.ReadSingleLittleEndian(bytes[12..]));

    public override void Encode(VolumeGapSample value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value.Key.X);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], value.Key.Y);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], value.Key.Z);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[12..], value.Value);
    }
}
