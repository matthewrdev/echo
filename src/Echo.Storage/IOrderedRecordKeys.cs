namespace Echo.Storage;

public interface IOrderedRecordKeys<TKey>
{
    IEnumerable<TKey> OrderedKeys();
}
