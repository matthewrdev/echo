namespace Echo.Storage.Records;

/// <summary>
/// Encodes fixed-width records stored in paged files and binary artifacts.
/// Implementations own the byte order, field layout and any auxiliary tables.
/// </summary>
/// <typeparam name="T">The in-memory record type.</typeparam>
public abstract class RecordCodec<T>
{
    /// <summary>Gets the encoded record width in bytes.</summary>
    public abstract int Size
    {
        get;
    }

    /// <summary>Decodes consecutive records into the supplied destination.</summary>
    public virtual void DecodeBlock(ReadOnlySpan<byte> bytes, Span<T> destination)
    {
        for (var index = 0; index < destination.Length; index++)
        {
            destination[index] = Decode(bytes.Slice(index * Size, Size));
        }
    }

    /// <summary>Encodes consecutive records into the supplied byte span.</summary>
    public virtual void EncodeBlock(ReadOnlySpan<T> source, Span<byte> bytes)
    {
        for (var index = 0; index < source.Length; index++)
        {
            Encode(source[index], bytes.Slice(index * Size, Size));
        }
    }

    /// <summary>
    /// Reports whether bytes produced by the source codec can be copied without re-encoding.
    /// Codecs with auxiliary tables must also account for the tables’ compatibility.
    /// </summary>
    public virtual bool CanCopyFrom(RecordCodec<T> source) => ReferenceEquals(this, source);

    /// <summary>Decodes one record from a span of at least <see cref="Size"/> bytes.</summary>
    public abstract T Decode(ReadOnlySpan<byte> bytes);

    /// <summary>Encodes one record into a span of at least <see cref="Size"/> bytes.</summary>
    public abstract void Encode(T value, Span<byte> bytes);
}
