using System.Buffers.Binary;
using Echo.Storage;

namespace Echo.Meshing;

internal sealed class ChartVertexCodec : RecordCodec<ChartVertex>
{
    private readonly StringTableCodec images = new();

    public override int Size => 8;

    public override ChartVertex Decode(ReadOnlySpan<byte> bytes) => new(BinaryPrimitives.ReadInt32LittleEndian(bytes), images.Decode(bytes[4..]));

    public override void Encode(ChartVertex value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value.Vertex);
        images.Encode(value.ImageId, bytes[4..]);
    }
}
