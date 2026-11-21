using System.Runtime.InteropServices;
using System.Buffers.Binary;
using Echo.Models;

namespace Echo.Storage;

public sealed class VolumeRecordCodec : RecordCodec<VolumeSample>
{
    public override void DecodeBlock(ReadOnlySpan<byte> bytes, Span<VolumeSample> destination)
    {
        if (BitConverter.IsLittleEndian)
        {
            bytes[..(destination.Length * Size)].CopyTo(MemoryMarshal.AsBytes(destination));
        }
        else
        {
            base.DecodeBlock(bytes, destination);
        }
    }

    public override void EncodeBlock(ReadOnlySpan<VolumeSample> source, Span<byte> bytes)
    {
        if (BitConverter.IsLittleEndian)
        {
            MemoryMarshal.AsBytes(source).CopyTo(bytes);
        }
        else
        {
            base.EncodeBlock(source, bytes);
        }
    }

    public override bool CanCopyFrom(RecordCodec<VolumeSample> source) => source is VolumeRecordCodec;

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
