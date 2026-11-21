using System.Buffers.Binary;
using System.Buffers;
using System.Collections;
using Echo.Storage.Records;
using Echo.Storage.Resources;

namespace Echo.Storage.Pages;

public sealed class PagedList<T> : IRecordList<T>, IDisposable
{
    private readonly PageFile file;
    private readonly RecordCodec<T> codec;
    private readonly long offset;
    private readonly bool header;
    private bool disposed;
    private int committedCount = -1;

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
            WriteHeader();
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
            committedCount = Count;
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

    public WorkBuffers CreateScratchBuffers() => new(file.Cache);

    public PageMemoryReservation? TryReserveMemory(long bytes)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return file.TryReserveMemory(bytes);
    }

    // Read whole records in this codec's binary layout, including dirty pages.
    public void ReadEncodedRecords(int start, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (destination.Length % codec.Size != 0)
        {
            throw new ArgumentException("The buffer must contain whole records.", nameof(destination));
        }

        if (start < 0 || start > Count - destination.Length / codec.Size)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        file.ReadBytes(offset + (long)start * codec.Size, destination);
    }

    // Copies the native record encoding, without the container header or page
    // padding. The destination must use this codec's layout. Reads include dirty
    // cached pages, so publication does not require an intermediate disk flush.
    public void CopyEncodedRecordsTo(Stream destination, CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            var remaining = (long)Count * codec.Size;
            var position = offset;
            while (remaining > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                var count = (int)Math.Min(buffer.Length, remaining);
                file.ReadBytes(position, buffer.AsSpan(0, count));
                destination.Write(buffer.AsSpan(0, count));
                position += count;
                remaining -= count;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
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

    public void AddRecord<TRecord>(TRecord value, RecordCodec<TRecord> recordCodec)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (recordCodec.Size != codec.Size)
        {
            throw new ArgumentException("The record layout must have the same size.", nameof(recordCodec));
        }

        Span<byte> bytes = stackalloc byte[codec.Size];
        recordCodec.Encode(value, bytes);
        AddEncodedRecords(bytes);
    }

    public void AddEncodedRecords(ReadOnlySpan<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (bytes.Length % codec.Size != 0)
        {
            throw new ArgumentException("The buffer must contain whole records.", nameof(bytes));
        }

        var count = bytes.Length / codec.Size;
        if (count > int.MaxValue - Count)
        {
            throw new InvalidDataException("Record count exceeds the supported index range.");
        }

        if (count > 0)
        {
            file.WriteBytes(offset + (long)Count * codec.Size, bytes);
            Count += count;
        }
    }

    public void ReadRange(int start, Span<T> destination)
    {
        ReadRange(start, destination, codec);
    }

    // The caller supplies a codec for the same binary layout, allowing compact
    // value records to be decoded without allocating an array for each record.
    public void ReadRange<TRecord>(int start, Span<TRecord> destination, RecordCodec<TRecord> recordCodec)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (recordCodec.Size != codec.Size)
        {
            throw new ArgumentException("The record layout must have the same size.", nameof(recordCodec));
        }

        if (start < 0 || start > Count - destination.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        if (destination.IsEmpty)
        {
            return;
        }

        if ((long)destination.Length * codec.Size <= 1024)
        {
            Span<byte> records = stackalloc byte[destination.Length * codec.Size];
            file.ReadBytes(offset + (long)start * codec.Size, records);
            recordCodec.DecodeBlock(records, destination);
            return;
        }

        var bytes = ArrayPool<byte>.Shared.Rent(Math.Max(codec.Size, 65536));
        try
        {
            while (!destination.IsEmpty)
            {
                var count = Math.Min(destination.Length, bytes.Length / codec.Size);
                file.ReadBytes(offset + (long)start * codec.Size, bytes.AsSpan(0, count * codec.Size));
                recordCodec.DecodeBlock(bytes.AsSpan(0, count * codec.Size), destination[..count]);
                destination = destination[count..];
                start += count;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    public void WriteRange(int start, ReadOnlySpan<T> source)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (start < 0 || start > Count - source.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        var bytes = ArrayPool<byte>.Shared.Rent(Math.Max(codec.Size, 65536));
        try
        {
            while (!source.IsEmpty)
            {
                var count = Math.Min(source.Length, bytes.Length / codec.Size);
                codec.EncodeBlock(source[..count], bytes.AsSpan(0, count * codec.Size));
                file.WriteBytes(offset + (long)start * codec.Size, bytes.AsSpan(0, count * codec.Size));
                start += count;
                source = source[count..];
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    public void AddRange(ReadOnlySpan<T> source)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (source.Length > int.MaxValue - Count)
        {
            throw new InvalidDataException("Record count exceeds the supported index range.");
        }

        var bytes = ArrayPool<byte>.Shared.Rent(Math.Max(codec.Size, 65536));
        try
        {
            while (!source.IsEmpty)
            {
                var count = Math.Min(source.Length, bytes.Length / codec.Size);
                codec.EncodeBlock(source[..count], bytes.AsSpan(0, count * codec.Size));
                file.WriteBytes(offset + (long)Count * codec.Size, bytes.AsSpan(0, count * codec.Size));
                Count += count;
                source = source[count..];
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    public void AddRepeated(T value, int count, CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        cancellation.ThrowIfCancellationRequested();
        if (count > int.MaxValue - Count)
        {
            throw new InvalidDataException("Record count exceeds the supported index range.");
        }

        if (count == 0)
        {
            return;
        }

        var bytes = ArrayPool<byte>.Shared.Rent(Math.Max(codec.Size, 65536));
        try
        {
            var capacity = Math.Min(count, bytes.Length / codec.Size);
            codec.Encode(value, bytes.AsSpan(0, codec.Size));
            var filled = 1;
            while (filled < capacity)
            {
                var copy = Math.Min(filled, capacity - filled);
                bytes.AsSpan(0, copy * codec.Size).CopyTo(bytes.AsSpan(filled * codec.Size));
                filled += copy;
            }

            while (count > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                var batch = Math.Min(capacity, count);
                file.WriteBytes(offset + (long)Count * codec.Size, bytes.AsSpan(0, batch * codec.Size));
                Count += batch;
                count -= batch;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    public void Commit()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        WriteHeader();
        file.Flush();
    }

    private void WriteHeader()
    {
        if (file.Writable && header && committedCount != Count)
        {
            Span<byte> bytes = stackalloc byte[16];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, 0x31504345);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], codec.Size);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[12..], Count);
            file.WriteBytes(0, bytes);
            committedCount = Count;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        try
        {
            if (file.Lifetime == PageFileLifetime.Persistent)
            {
                WriteHeader();
            }
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
