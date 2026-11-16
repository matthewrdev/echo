using System.Collections;
using Microsoft.Data.Sqlite;

namespace Echo.Storage;

public sealed class DiskDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IOrderedRecordKeys<TKey> where TKey : notnull
{
    private readonly SqliteConnection connection;
    private readonly string table;
    private readonly RecordCodec<TKey> keys;
    private readonly RecordCodec<TValue> values;
    private readonly SqliteCommand lookup;
    private readonly SqliteCommand insert;
    private readonly SqliteCommand update;
    private readonly byte[] keyBytes;
    private readonly byte[] valueBytes;

    public DiskDictionary(SqliteConnection connection, string table, RecordCodec<TKey> keys, RecordCodec<TValue> values)
    {
        this.connection = connection;
        this.table = table;
        this.keys = keys;
        this.values = values;
        keyBytes = new byte[keys.Size];
        valueBytes = new byte[values.Size];
        using var create = connection.CreateCommand();
        create.CommandText = $"CREATE TABLE {table}(seq INTEGER PRIMARY KEY, key BLOB NOT NULL UNIQUE, value BLOB NOT NULL)";
        create.ExecuteNonQuery();
        lookup = Command($"SELECT value FROM {table} WHERE key=$key", false);
        insert = Command($"INSERT INTO {table}(key,value) VALUES($key,$value) ON CONFLICT(key) DO NOTHING", true);
        update = Command($"UPDATE {table} SET value=$value WHERE key=$key", true);
    }

    public int Count
    {
        get; private set;
    }
    public bool IsReadOnly => false;
    public ICollection<TKey> Keys => new DiskDictionaryKeys<TKey, TValue>(this);
    public ICollection<TValue> Values => throw new NotSupportedException("Enumerate records without materialising values.");

    public TValue this[TKey key]
    {
        get => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException();
        set
        {
            if (!TryAdd(key, value))
            {
                update.ExecuteNonQuery();
            }
        }
    }

    public bool TryAdd(TKey key, TValue value)
    {
        keys.Encode(key, keyBytes);
        values.Encode(value, valueBytes);
        var added = insert.ExecuteNonQuery() != 0;
        if (added)
        {
            Count = checked(Count + 1);
        }
        return added;
    }

    public void Add(TKey key, TValue value)
    {
        if (!TryAdd(key, value))
        {
            throw new ArgumentException("Duplicate key.", nameof(key));
        }
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        keys.Encode(key, keyBytes);
        if (lookup.ExecuteScalar() is byte[] bytes)
        {
            value = values.Decode(bytes);
            return true;
        }
        value = default!;
        return false;
    }

    public bool ContainsKey(TKey key) => TryGetValue(key, out _);
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT key,value FROM {table} ORDER BY seq";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            yield return new(keys.Decode((byte[])reader[0]), values.Decode((byte[])reader[1]));
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public IEnumerable<TKey> OrderedKeys()
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT key FROM {table} ORDER BY key";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            yield return keys.Decode((byte[])reader[0]);
        }
    }
    public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);
    public bool Contains(KeyValuePair<TKey, TValue> item) => TryGetValue(item.Key, out var value) && EqualityComparer<TValue>.Default.Equals(value, item.Value);
    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        foreach (var item in this)
        {
            array[arrayIndex++] = item;
        }
    }
    public void Clear()
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {table}";
        command.ExecuteNonQuery();
        Count = 0;
    }
    public bool Remove(TKey key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {table} WHERE key=$key";
        var bytes = new byte[keys.Size];
        keys.Encode(key, bytes);
        command.Parameters.AddWithValue("$key", bytes);
        var removed = command.ExecuteNonQuery() != 0;
        if (removed)
        {
            Count--;
        }
        return removed;
    }
    public bool Remove(KeyValuePair<TKey, TValue> item) => Contains(item) && Remove(item.Key);

    private SqliteCommand Command(string sql, bool withValue)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$key", keyBytes);
        if (withValue)
        {
            command.Parameters.AddWithValue("$value", valueBytes);
        }
        command.Prepare();
        return command;
    }
}
