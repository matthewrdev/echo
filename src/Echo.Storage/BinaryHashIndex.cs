using System.Collections;

namespace Echo.Storage;

// Scratch lookup with stable insertion order. Buckets and collision chains use
// the same bounded page cache as geometry; no database or per-lookup allocation.
// A caller supplies the expected size to avoid rebuilding large indexes.
public sealed class BinaryHashIndex<TKey, TValue> : IReadOnlyDictionary<TKey, TValue> where TKey : notnull
{
    private readonly IList<int> buckets;
    private readonly IList<HashIndexEntry<TKey, TValue>> entries;
    private readonly EqualityComparer<TKey> comparer = EqualityComparer<TKey>.Default;

    public BinaryHashIndex(int expectedCount, RecordCodec<TKey> keys, RecordCodec<TValue> values,
        WorkBuffers? work = null, CancellationToken cancellation = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedCount);
        cancellation.ThrowIfCancellationRequested();
        var count = 16;
        while (count < expectedCount && count < 1 << 26)
        {
            count *= 2;
        }
        buckets = work?.List(new IntRecordCodec()) ?? new List<int>(count);
        for (var index = 0; index < count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            buckets.Add(-1);
        }
        entries = work?.List(new HashIndexEntryCodec<TKey, TValue>(keys, values)) ?? new List<HashIndexEntry<TKey, TValue>>();
    }

    public int Count => entries.Count;

    public IEnumerable<TKey> Keys
    {
        get
        {
            for (var index = 0; index < entries.Count; index++)
            {
                yield return entries[index].Key;
            }
        }
    }

    public IEnumerable<TValue> Values
    {
        get
        {
            for (var index = 0; index < entries.Count; index++)
            {
                yield return entries[index].Value;
            }
        }
    }

    public TValue this[TKey key]
    {
        get => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException();
        set
        {
            var bucket = Bucket(key);
            var head = buckets[bucket];
            var index = Find(key, head, out var entry);
            if (index >= 0)
            {
                entries[index] = entry with
                {
                    Value = value
                };
            }
            else
            {
                Append(key, value, bucket, head);
            }
        }
    }

    public bool TryAdd(TKey key, TValue value)
    {
        var bucket = Bucket(key);
        var head = buckets[bucket];
        if (Find(key, head, out _) >= 0)
        {
            return false;
        }
        Append(key, value, bucket, head);
        return true;
    }

    public void Add(TKey key, TValue value)
    {
        if (!TryAdd(key, value))
        {
            throw new ArgumentException("Duplicate key.", nameof(key));
        }
    }

    public bool ContainsKey(TKey key) => Find(key, buckets[Bucket(key)], out _) >= 0;

    public bool TryGetValue(TKey key, out TValue value)
    {
        var found = Find(key, buckets[Bucket(key)], out var entry) >= 0;
        value = found ? entry.Value : default!;
        return found;
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            yield return new KeyValuePair<TKey, TValue>(entry.Key, entry.Value);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void Append(TKey key, TValue value, int bucket, int head)
    {
        var index = entries.Count;
        entries.Add(new HashIndexEntry<TKey, TValue>(key, value, head));
        buckets[bucket] = index;
    }

    private int Find(TKey key, int index, out HashIndexEntry<TKey, TValue> entry)
    {
        while (index >= 0)
        {
            entry = entries[index];
            if (comparer.Equals(entry.Key, key))
            {
                return index;
            }
            index = entry.Next;
        }
        entry = default;
        return -1;
    }

    private int Bucket(TKey key)
    {
        var hash = unchecked((uint)comparer.GetHashCode(key));
        hash ^= hash >> 16;
        hash *= 0x85ebca6bu;
        hash ^= hash >> 13;
        return (int)(hash & (uint)(buckets.Count - 1));
    }
}
