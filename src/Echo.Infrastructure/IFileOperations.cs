namespace Echo.Infrastructure;

public interface IFileOperations
{
    string HashFile(string file);
    string Hash(object value);
    string HashGenerated<T>(T value);
    void AtomicData<T>(string path, T value, CancellationToken cancellation = default);
    void AtomicJson<T>(string path, T value);
    string? AtomicWrite(string path, Action<Stream> write);
    void CopyVerified(string source, string destination, string checksum);
    void AtomicBytes(string path, byte[] bytes);
}
