using System.Buffers;
using System.Runtime.CompilerServices;

namespace Echo.Storage;

public static class RecordListOperations
{
    public static void Read<T>(IList<T> source, int start, Span<T> destination)
    {
        if (source is IRecordList<T> records)
        {
            records.ReadRange(start, destination);
        }
        else if (source is T[] array)
        {
            array.AsSpan(start, destination.Length).CopyTo(destination);
        }
        else
        {
            for (var index = 0; index < destination.Length; index++)
            {
                destination[index] = source[start + index];
            }
        }
    }

    public static void Write<T>(IList<T> destination, int start, ReadOnlySpan<T> source)
    {
        if (destination is IRecordList<T> records)
        {
            records.WriteRange(start, source);
        }
        else if (destination is T[] array)
        {
            source.CopyTo(array.AsSpan(start, source.Length));
        }
        else
        {
            for (var index = 0; index < source.Length; index++)
            {
                destination[start + index] = source[index];
            }
        }
    }

    public static void Fill<T>(IList<T> destination, int count, T value)
    {
        if (destination is T[] array)
        {
            array.AsSpan(0, count).Fill(value);
            return;
        }
        var buffer = ArrayPool<T>.Shared.Rent(Math.Min(4096, Math.Max(1, count)));
        try
        {
            buffer.AsSpan().Fill(value);
            for (var start = 0; start < count; start += buffer.Length)
            {
                Write(destination, start, buffer.AsSpan(0, Math.Min(buffer.Length, count - start)));
            }
        }
        finally
        {
            ArrayPool<T>.Shared.Return(buffer, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
    }

    public static void Copy<T>(IList<T> source, IList<T> destination, int count)
    {
        var buffer = ArrayPool<T>.Shared.Rent(Math.Min(4096, Math.Max(1, count)));
        try
        {
            for (var start = 0; start < count; start += buffer.Length)
            {
                var length = Math.Min(buffer.Length, count - start);
                Read(source, start, buffer.AsSpan(0, length));
                Write(destination, start, buffer.AsSpan(0, length));
            }
        }
        finally
        {
            ArrayPool<T>.Shared.Return(buffer, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
    }
}
