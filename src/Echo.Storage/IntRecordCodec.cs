using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Echo.Models;

namespace Echo.Storage;

public sealed class IntRecordCodec : RecordCodec<int>
{
    public override bool CanCopyFrom(RecordCodec<int> source) => source is IntRecordCodec;

    public override void DecodeBlock(ReadOnlySpan<byte> bytes, Span<int> destination)
    {
        if (BitConverter.IsLittleEndian)
        {
            MemoryMarshal.Cast<byte, int>(bytes)[..destination.Length].CopyTo(destination);
        }
        else
        {
            base.DecodeBlock(bytes, destination);
        }
    }

    public override int Size => 4;
    public override int Decode(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadInt32LittleEndian(bytes);
    public override void Encode(int value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
    }
}
