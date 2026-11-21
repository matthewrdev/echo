using System.Buffers.Binary;
using Echo.Storage.Records;
using Echo.Storage;

namespace Echo.Meshing.Volumes;

internal sealed class GridRecordCodec : RecordCodec<Grid>
{
    public override int Size => 12;
    public override Grid Decode(ReadOnlySpan<byte> bytes) => new(
        BinaryPrimitives.ReadInt32BigEndian(bytes) ^ int.MinValue,
        BinaryPrimitives.ReadInt32BigEndian(bytes[4..]) ^ int.MinValue,
        BinaryPrimitives.ReadInt32BigEndian(bytes[8..]) ^ int.MinValue);
    public override void Encode(Grid value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteInt32BigEndian(bytes, value.X ^ int.MinValue);
        BinaryPrimitives.WriteInt32BigEndian(bytes[4..], value.Y ^ int.MinValue);
        BinaryPrimitives.WriteInt32BigEndian(bytes[8..], value.Z ^ int.MinValue);
    }
}
