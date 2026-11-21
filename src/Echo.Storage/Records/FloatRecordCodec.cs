using System.Runtime.InteropServices;
using System.Buffers.Binary;
using Echo.Models;

namespace Echo.Storage.Records;

public sealed class FloatRecordCodec : RecordCodec<float>
{
    public override void DecodeBlock(ReadOnlySpan<byte> bytes, Span<float> destination)
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

    public override void EncodeBlock(ReadOnlySpan<float> source, Span<byte> bytes)
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

    public override bool CanCopyFrom(RecordCodec<float> source) => source is FloatRecordCodec;

    public override int Size => 4;
    public override float Decode(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadSingleLittleEndian(bytes);
    public override void Encode(float value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
    }
}
