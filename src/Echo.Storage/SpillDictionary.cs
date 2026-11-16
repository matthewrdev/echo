using System.Collections;

namespace Echo.Storage;

// Small indices stay in the standard Dictionary. Exceeding the reserved entry
// capacity transfers them in insertion order to the disk index, once only.
public sealed class SpillDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IOrderedRecordKeys<TKey> where TKey : notnull
{
    private readonly Func<DiskDictionary<TKey, TValue>> create;
    private readonly int capacity;
    private IDictionary<TKey, TValue> records = new Dictionary<TKey, TValue>();
    private DiskDictionary<TKey, TValue>? disk;

    public SpillDictionary(int capacity, Func<DiskDictionary<TKey, TValue>> create)
    {
        this.capacity = capacity;
        this.create = create;
    }

    public int Count => records.Count;
    public bool IsReadOnly => false;
    public ICollection<TKey> Keys => records.Keys;
    public ICollection<TValue> Values => records.Values;

    public TValue this[TKey key]
    {
        get => records[key];
        set
        {
            if (!records.ContainsKey(key))
            {
                EnsureCapacity();
            }
            records[key] = value;
        }
    }

    public void Add(TKey key, TValue value)
    {
        EnsureCapacity();
        records.Add(key, value);
    }

    public bool TryAdd(TKey key, TValue value)
    {
        EnsureCapacity();
        return disk != null ? disk.TryAdd(key, value) : ((Dictionary<TKey, TValue>)records).TryAdd(key, value);
    }

    public bool ContainsKey(TKey key) => records.ContainsKey(key);
    public bool TryGetValue(TKey key, out TValue value) => records.TryGetValue(key, out value!);
    public bool Remove(TKey key) => records.Remove(key);
    public void Clear() => records.Clear();
    public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);
    public bool Contains(KeyValuePair<TKey, TValue> item) => records.Contains(item);
    public bool Remove(KeyValuePair<TKey, TValue> item) => records.Remove(item);
    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) => records.CopyTo(array, arrayIndex);
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => records.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerable<TKey> OrderedKeys() => Spill().OrderedKeys();

    private void EnsureCapacity()
    {
        if (disk == null && records.Count >= capacity)
        {
            Spill();
        }
    }

    private DiskDictionary<TKey, TValue> Spill()
    {
        if (disk == null)
        {
            var target = create();
            foreach (var pair in records)
            {
                target.Add(pair.Key, pair.Value);
            }
            disk = target;
            records = target;
        }
        return disk;
    }
}
