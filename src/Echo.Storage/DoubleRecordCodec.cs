using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Echo.Storage;

public sealed class DoubleRecordCodec : RecordCodec<double>
{
    public override bool CanCopyFrom(RecordCodec<double> source) => source is DoubleRecordCodec;

    public override void DecodeBlock(ReadOnlySpan<byte> bytes, Span<double> destination)
    {
        if (BitConverter.IsLittleEndian)
        {
            MemoryMarshal.Cast<byte, double>(bytes)[..destination.Length].CopyTo(destination);
        }
        else
        {
            base.DecodeBlock(bytes, destination);
        }
    }

    public override int Size => 8;
    public override double Decode(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadDoubleLittleEndian(bytes);
    public override void Encode(double value, Span<byte> bytes) => BinaryPrimitives.WriteDoubleLittleEndian(bytes, value);
}
