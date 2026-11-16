using System.Collections;

namespace Echo.Storage;

internal sealed class DiskDictionaryKeys<TKey, TValue> : ICollection<TKey> where TKey : notnull
{
    private readonly DiskDictionary<TKey, TValue> dictionary;
    public DiskDictionaryKeys(DiskDictionary<TKey, TValue> dictionary)
    {
        this.dictionary = dictionary;
    }
    public int Count => dictionary.Count;
    public bool IsReadOnly => true;
    public bool Contains(TKey item) => dictionary.ContainsKey(item);
    public IEnumerator<TKey> GetEnumerator() => dictionary.Select(entry => entry.Key).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public void CopyTo(TKey[] array, int arrayIndex)
    {
        foreach (var key in this)
        {
            array[arrayIndex++] = key;
        }
    }
    public void Add(TKey item) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public bool Remove(TKey item) => throw new NotSupportedException();
}
