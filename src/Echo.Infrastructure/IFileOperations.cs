namespace Echo.Infrastructure;

public interface IFileOperations
{
    string HashFile(string file);
    string Hash(object value);
    void AtomicJson<T>(string path, T value);
    void AtomicWrite(string path, Action<Stream> write);
    void AtomicBytes(string path, byte[] bytes);
}
