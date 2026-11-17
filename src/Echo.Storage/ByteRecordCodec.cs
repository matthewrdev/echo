namespace Echo.Storage;

public sealed class ByteRecordCodec : RecordCodec<byte>
{
    public override bool CanCopyFrom(RecordCodec<byte> source) => source is ByteRecordCodec;

    public override int Size => 1;
    public override byte Decode(ReadOnlySpan<byte> bytes) => bytes[0];
    public override void Encode(byte value, Span<byte> bytes) => bytes[0] = value;
}
