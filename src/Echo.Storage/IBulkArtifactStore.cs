namespace Echo.Storage;

public interface IBulkArtifactStore
{
    bool CanStore<T>(T value);
    void Write<T>(string path, T value, CancellationToken cancellation = default);
    bool Supports(Type type);
    T Read<T>(string path);
    BulkDataSession OpenSession(long memoryBytes, long diskBytes);
}
