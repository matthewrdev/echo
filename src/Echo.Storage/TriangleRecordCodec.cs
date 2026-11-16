using System.Buffers.Binary;
using Echo.Contracts;

namespace Echo.Storage;

public sealed class TriangleRecordCodec : RecordCodec<int[]>
{
    public override int Size => 12;
    public override int[] Decode(ReadOnlySpan<byte> bytes) => [BinaryPrimitives.ReadInt32LittleEndian(bytes), BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[8..])];
    public override void Encode(int[] value, Span<byte> bytes)
    {
        for (var i = 0; i < 3; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes[(i * 4)..], value[i]);
        }
    }
}
