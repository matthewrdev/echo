namespace Echo.Storage;

public abstract class RecordCodec<T>
{
    public abstract int Size
    {
        get;
    }
    public virtual void DecodeBlock(ReadOnlySpan<byte> bytes, Span<T> destination)
    {
        for (var index = 0; index < destination.Length; index++)
        {
            destination[index] = Decode(bytes.Slice(index * Size, Size));
        }
    }

    public virtual void EncodeBlock(ReadOnlySpan<T> source, Span<byte> bytes)
    {
        for (var index = 0; index < source.Length; index++)
        {
            Encode(source[index], bytes.Slice(index * Size, Size));
        }
    }

    public abstract T Decode(ReadOnlySpan<byte> bytes);
    public abstract void Encode(T value, Span<byte> bytes);
}
