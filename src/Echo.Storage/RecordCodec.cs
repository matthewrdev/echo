namespace Echo.Storage;

public abstract class RecordCodec<T>
{
    public abstract int Size
    {
        get;
    }
    public abstract T Decode(ReadOnlySpan<byte> bytes);
    public abstract void Encode(T value, Span<byte> bytes);
}
