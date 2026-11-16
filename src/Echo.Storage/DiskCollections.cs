using Microsoft.Data.Sqlite;

namespace Echo.Storage;

// This database is disposable scratch, never a published checkpoint. Durable
// stage/chunk publication uses the checkpoint store and synced binary buffers.
public sealed class DiskCollections : IDisposable
{
    private readonly SqliteConnection connection;
    private int nextTable;
    private readonly string path;

    public long DiskBytes => new FileInfo(path).Length;

    public DiskCollections(string path, long cacheBytes, long diskBytes)
    {
        this.path = path;
        connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString());
        connection.Open();
        using var setup = connection.CreateCommand();
        setup.CommandText = $"PRAGMA cache_size=-{Math.Max(4, cacheBytes / 1024)}; PRAGMA mmap_size=0; PRAGMA temp_store=FILE; PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF;";
        if (diskBytes > 0)
        {
            setup.CommandText += $" PRAGMA max_page_count={Math.Max(1, diskBytes / 4096)};";
        }
        setup.ExecuteNonQuery();
        // Scratch has no recovery contract; one transaction avoids a commit per
        // voxel while SQLite still evicts dirty pages within its cache budget.
        setup.CommandText = "BEGIN";
        setup.ExecuteNonQuery();
    }

    public DiskDictionary<TKey, TValue> Create<TKey, TValue>(RecordCodec<TKey> keys, RecordCodec<TValue> values) where TKey : notnull
    {
        return new DiskDictionary<TKey, TValue>(connection, "buffer" + nextTable++, keys, values);
    }

    public void Dispose() => connection.Dispose();
}
