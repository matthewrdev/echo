using System.Buffers.Binary;

namespace Echo.Storage;

public sealed class DoubleRecordCodec : RecordCodec<double>
{
    public override int Size => 8;
    public override double Decode(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadDoubleLittleEndian(bytes);
    public override void Encode(double value, Span<byte> bytes) => BinaryPrimitives.WriteDoubleLittleEndian(bytes, value);
}
