using System.Buffers.Binary;
using System.Collections;

namespace Echo.Storage;

public sealed class PagedList<T> : IList<T>, IReadOnlyList<T>, IDisposable
{
    private readonly PageFile file;
    private readonly RecordCodec<T> codec;
    private readonly long offset;
    private readonly bool header;
    private bool disposed;

    public PagedList(PageFile file, RecordCodec<T> codec, long rawOffset = -1, int rawCount = 0)
    {
        this.file = file;
        this.codec = codec;
        header = rawOffset < 0;
        offset = header ? 16 : rawOffset;
        if (!header)
        {
            Count = rawCount;
        }
        else if (file.Length == 0 && file.Writable)
        {
            Commit();
        }
        else
        {
            Span<byte> bytes = stackalloc byte[16];
            file.ReadBytes(0, bytes);
            var valid = BinaryPrimitives.ReadInt32LittleEndian(bytes) == 0x31504345
                && BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]) == 1
                && BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]) == codec.Size;
            if (!valid)
            {
                throw new InvalidDataException("Unsupported binary record header.");
            }
            Count = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
        }
        if (Count < 0 || offset + (long)Count * codec.Size > file.Length)
        {
            throw new InvalidDataException("Truncated binary records.");
        }
    }

    public int Count
    {
        get; private set;
    }
    public bool IsReadOnly => !file.Writable;
    public RecordCodec<T> Codec => codec;

    // Copies the native record encoding, without the container header or page
    // padding. The destination must use this codec's layout. Reads include dirty
    // cached pages, so publication does not require an intermediate disk flush.
    public void CopyEncodedRecordsTo(Stream destination)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var buffer = new byte[65536];
        var remaining = (long)Count * codec.Size;
        var position = offset;
        while (remaining > 0)
        {
            var count = (int)Math.Min(buffer.Length, remaining);
            file.ReadBytes(position, buffer.AsSpan(0, count));
            destination.Write(buffer, 0, count);
            position += count;
            remaining -= count;
        }
    }

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            Span<byte> bytes = stackalloc byte[codec.Size];
            file.ReadBytes(offset + (long)index * codec.Size, bytes);
            return codec.Decode(bytes);
        }
        set
        {
            if ((uint)index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            Write(index, value);
        }
    }

    public void Add(T value)
    {
        if (Count == int.MaxValue)
        {
            throw new InvalidDataException("Record count exceeds the supported index range.");
        }
        Write(Count, value);
        Count++;
    }

    public void Commit()
    {
        if (file.Writable && header)
        {
            Span<byte> bytes = stackalloc byte[16];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, 0x31504345);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], codec.Size);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[12..], Count);
            file.WriteBytes(0, bytes);
        }
        file.Flush();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        try
        {
            Commit();
        }
        finally
        {
            disposed = true;
            file.Dispose();
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
        {
            yield return this[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public void Clear()
    {
        if (IsReadOnly)
        {
            throw new NotSupportedException();
        }
        Count = 0;
    }

    public int IndexOf(T item)
    {
        for (var index = 0; index < Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(this[index], item))
            {
                return index;
            }
        }
        return -1;
    }

    public bool Contains(T item) => IndexOf(item) >= 0;
    public void CopyTo(T[] array, int arrayIndex)
    {
        foreach (var value in this)
        {
            array[arrayIndex++] = value;
        }
    }

    public void Insert(int index, T item)
    {
        if ((uint)index > (uint)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        Add(item);
        for (var move = Count - 1; move > index; move--)
        {
            this[move] = this[move - 1];
        }
        this[index] = item;
    }

    public void RemoveAt(int index)
    {
        if (IsReadOnly || (uint)index >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        for (var move = index; move < Count - 1; move++)
        {
            this[move] = this[move + 1];
        }
        Count--;
    }

    public bool Remove(T item)
    {
        var index = IndexOf(item);
        if (index < 0)
        {
            return false;
        }
        RemoveAt(index);
        return true;
    }

    private void Write(int index, T value)
    {
        Span<byte> bytes = stackalloc byte[codec.Size];
        codec.Encode(value, bytes);
        file.WriteBytes(offset + (long)index * codec.Size, bytes);
    }
}
