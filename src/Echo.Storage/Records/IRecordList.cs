namespace Echo.Storage.Records;

public interface IRecordList<T> : IList<T>, IReadOnlyList<T>
{
    void ReadRange(int start, Span<T> destination);
    void WriteRange(int start, ReadOnlySpan<T> source);
}
