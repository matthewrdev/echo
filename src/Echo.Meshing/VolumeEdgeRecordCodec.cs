using Echo.Storage;

namespace Echo.Meshing;

internal sealed class VolumeEdgeRecordCodec : RecordCodec<VolumeEdge>
{
    private readonly GridRecordCodec grid = new();
    public override int Size => 24;
    public override VolumeEdge Decode(ReadOnlySpan<byte> bytes) => new(grid.Decode(bytes), grid.Decode(bytes[12..]));
    public override void Encode(VolumeEdge value, Span<byte> bytes)
    {
        grid.Encode(value.A, bytes);
        grid.Encode(value.B, bytes[12..]);
    }
}
