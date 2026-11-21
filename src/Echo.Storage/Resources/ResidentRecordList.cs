using System.Collections;
using System.Runtime.CompilerServices;
using Echo.Storage.Artifacts;
using Echo.Storage.Pages;
using Echo.Storage.Records;

namespace Echo.Storage.Resources;

// Typed scratch records share the page-cache budget. If that budget is exhausted,
// spill this list once and keep using the bounded paging backend thereafter.
internal sealed class ResidentRecordList<T> : IRecordList<T>, IDisposable
{
    private const int blockSize = 4096;
    private readonly BulkDataSession session;
    private readonly string path;
    private readonly RecordCodec<T> codec;
    private readonly List<T[]> blocks = [];
    private readonly List<PageMemoryReservation> reservations = [];
    private PagedList<T>? paged;
    private int count;
    private bool disposed;

    public ResidentRecordList(BulkDataSession session, string path, RecordCodec<T> codec)
    {
        this.session = session;
        this.path = path;
        this.codec = codec;
    }

    public int Count => paged?.Count ?? count;
    public bool IsReadOnly => false;

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return paged != null ? paged[index] : blocks[index / blockSize][index % blockSize];
        }
        set
        {
            if ((uint)index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (paged != null)
            {
                paged[index] = value;
            }
            else
            {
                blocks[index / blockSize][index % blockSize] = value;
            }
        }
    }

    public void ReadRange(int start, Span<T> destination)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (start < 0 || start > Count - destination.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        if (paged != null)
        {
            paged.ReadRange(start, destination);
            return;
        }

        while (!destination.IsEmpty)
        {
            var length = Math.Min(destination.Length, blockSize - start % blockSize);
            blocks[start / blockSize].AsSpan(start % blockSize, length).CopyTo(destination);
            start += length;
            destination = destination[length..];
        }
    }

    public void WriteRange(int start, ReadOnlySpan<T> source)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (start < 0 || start > Count - source.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        if (paged != null)
        {
            paged.WriteRange(start, source);
            return;
        }

        while (!source.IsEmpty)
        {
            var length = Math.Min(source.Length, blockSize - start % blockSize);
            source[..length].CopyTo(blocks[start / blockSize].AsSpan(start % blockSize, length));
            start += length;
            source = source[length..];
        }
    }

    public void Add(T value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Count == int.MaxValue)
        {
            throw new InvalidDataException("Record count exceeds the supported index range.");
        }

        if (paged == null && count == blocks.Count * (long)blockSize)
        {
            var reservation = session.TryReserveMemory(blockSize * (long)Unsafe.SizeOf<T>() + 128);
            if (reservation == null)
            {
                Spill();
            }
            else
            {
                try
                {
                    blocks.Add(new T[blockSize]);
                    reservations.Add(reservation);
                }
                catch
                {
                    reservation.Dispose();
                    throw;
                }
            }
        }

        if (paged != null)
        {
            paged.Add(value);
        }
        else
        {
            blocks[count / blockSize][count % blockSize] = value;
            count++;
        }
    }

    private void Spill()
    {
        paged = session.OpenTemporary(path, codec);
        for (var index = 0; index < blocks.Count; index++)
        {
            var length = Math.Min(blockSize, count - index * blockSize);
            paged.AddRange(blocks[index].AsSpan(0, length));
            blocks[index] = [];
            reservations[index].Dispose();
        }

        blocks.Clear();
        reservations.Clear();
    }

    public void Clear()
    {
        paged?.Clear();
        count = 0;
        ReleaseMemory();
    }

    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (paged != null)
        {
            paged.RemoveAt(index);
            return;
        }

        for (var move = index; move < count - 1; move++)
        {
            this[move] = this[move + 1];
        }

        count--;
        blocks[count / blockSize][count % blockSize] = default!;
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

    public void CopyTo(T[] array, int arrayIndex)
    {
        foreach (var item in this)
        {
            array[arrayIndex++] = item;
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

    private void ReleaseMemory()
    {
        blocks.Clear();
        foreach (var reservation in reservations)
        {
            reservation.Dispose();
        }

        reservations.Clear();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try
        {
            paged?.Dispose();
        }
        finally
        {
            count = 0;
            ReleaseMemory();
        }
    }
}
