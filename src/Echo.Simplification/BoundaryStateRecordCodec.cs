using Echo.Storage;

namespace Echo.Simplification;

internal sealed class BoundaryStateRecordCodec : RecordCodec<BoundaryState>
{
    public override int Size => 1;
    public override BoundaryState Decode(ReadOnlySpan<byte> bytes) => (BoundaryState)bytes[0];
    public override void Encode(BoundaryState value, Span<byte> bytes) => bytes[0] = (byte)value;
}
