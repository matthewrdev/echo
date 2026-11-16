using System.Buffers.Binary;
using Echo.Contracts;

namespace Echo.Storage;

public sealed class IntRecordCodec : RecordCodec<int>
{
    public override int Size => 4;
    public override int Decode(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadInt32LittleEndian(bytes);
    public override void Encode(int value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
    }
}
