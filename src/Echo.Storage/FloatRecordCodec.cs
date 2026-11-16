using System.Buffers.Binary;
using Echo.Contracts;

namespace Echo.Storage;

public sealed class FloatRecordCodec : RecordCodec<float>
{
    public override int Size => 4;
    public override float Decode(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadSingleLittleEndian(bytes);
    public override void Encode(float value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
    }
}
