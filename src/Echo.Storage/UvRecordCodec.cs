using System.Buffers.Binary;

namespace Echo.Storage;

public sealed class UvRecordCodec : RecordCodec<double[]>
{
    public override int Size => 16;
    public override double[] Decode(ReadOnlySpan<byte> bytes) => [BinaryPrimitives.ReadDoubleLittleEndian(bytes), BinaryPrimitives.ReadDoubleLittleEndian(bytes[8..])];
    public override void Encode(double[] value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteDoubleLittleEndian(bytes, value[0]);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[8..], value[1]);
    }
}
