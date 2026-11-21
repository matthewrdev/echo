using System.Buffers.Binary;
using Echo.Geometry;
using Echo.Storage.Records;
using Echo.Storage;

namespace Echo.Geometry;

// Dictionary<double> equality identifies signed zeros and all NaNs. Disk keys
// must use the same equality so storage mode cannot change welded connectivity.
public sealed class PositionKeyCodec : RecordCodec<V3>
{
    public override int Size => 24;
    public override V3 Decode(ReadOnlySpan<byte> bytes) => new(BinaryPrimitives.ReadDoubleLittleEndian(bytes), BinaryPrimitives.ReadDoubleLittleEndian(bytes[8..]), BinaryPrimitives.ReadDoubleLittleEndian(bytes[16..]));

    public override void Encode(V3 value, Span<byte> bytes)
    {
        Write(value.X, bytes);
        Write(value.Y, bytes[8..]);
        Write(value.Z, bytes[16..]);
    }

    private void Write(double value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteDoubleLittleEndian(bytes, value == 0 ? 0d : double.IsNaN(value) ? double.NaN : value);
    }
}
