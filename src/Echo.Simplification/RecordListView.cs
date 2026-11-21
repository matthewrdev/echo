using System.Collections;

namespace Echo.Simplification;

// Surface contracts use arrays; keep partition storage in compact value records
// and materialise only the record requested by the simplifier.
internal sealed class RecordListView<TRecord, TValue> : IList<TValue>
{
    private readonly IList<TRecord> records;
    private readonly Func<TRecord, TValue> convert;

    public RecordListView(IList<TRecord> records, Func<TRecord, TValue> convert)
    {
        this.records = records;
        this.convert = convert;
    }

    public int Count => records.Count;
    public bool IsReadOnly => true;
    public TValue this[int index]
    {
        get => convert(records[index]);
        set => throw new NotSupportedException();
    }

    public IEnumerator<TValue> GetEnumerator()
    {
        foreach (var record in records)
        {
            yield return convert(record);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public void CopyTo(TValue[] array, int arrayIndex)
    {
        foreach (var value in this)
        {
            array[arrayIndex++] = value;
        }
    }

    public int IndexOf(TValue item)
    {
        for (var index = 0; index < Count; index++)
        {
            if (EqualityComparer<TValue>.Default.Equals(this[index], item))
            {
                return index;
            }
        }

        return -1;
    }

    public bool Contains(TValue item) => IndexOf(item) >= 0;
    public void Add(TValue item) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, TValue item) => throw new NotSupportedException();
    public bool Remove(TValue item) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
}
