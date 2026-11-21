namespace Echo.Storage.Records;

public sealed class BoolRecordCodec : RecordCodec<bool>
{
    public override bool CanCopyFrom(RecordCodec<bool> source) => source is BoolRecordCodec;

    public override int Size => 1;
    public override bool Decode(ReadOnlySpan<byte> bytes) => bytes[0] != 0;
    public override void Encode(bool value, Span<byte> bytes) => bytes[0] = value ? (byte)1 : (byte)0;
}
