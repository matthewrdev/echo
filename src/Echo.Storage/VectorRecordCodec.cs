using System.Buffers.Binary;
using Echo.Contracts;

namespace Echo.Storage;

public sealed class VectorRecordCodec : RecordCodec<double[]>
{
    public override int Size => 24;
    public override double[] Decode(ReadOnlySpan<byte> bytes) => [BinaryPrimitives.ReadDoubleLittleEndian(bytes), BinaryPrimitives.ReadDoubleLittleEndian(bytes[8..]), BinaryPrimitives.ReadDoubleLittleEndian(bytes[16..])];
    public override void Encode(double[] value, Span<byte> bytes)
    {
        for (var i = 0; i < 3; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes[(i * 8)..], value[i]);
        }
    }
}
