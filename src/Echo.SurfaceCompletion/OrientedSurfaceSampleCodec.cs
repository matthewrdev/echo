using Echo.Geometry;
using Echo.Storage.Records;
using Echo.Storage;

namespace Echo.SurfaceCompletion;

internal sealed class OrientedSurfaceSampleCodec : RecordCodec<OrientedSurfaceSample>
{
    private readonly Vector3RecordCodec vectors = new();

    public override int Size => 48;
    public override OrientedSurfaceSample Decode(ReadOnlySpan<byte> bytes) => new(vectors.Decode(bytes), vectors.Decode(bytes[24..]));

    public override void Encode(OrientedSurfaceSample value, Span<byte> bytes)
    {
        vectors.Encode(value.Position, bytes);
        vectors.Encode(value.Normal, bytes[24..]);
    }
}
