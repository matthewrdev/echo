using System.Text.Json.Serialization.Metadata;

namespace Echo.Storage;

public interface IBinaryArtifactFiles
{
    string WritePayload(string path, int kind, Action<Stream> write);
    void WriteHeader<T>(string path, T header, JsonTypeInfo<T> type);
    FileStream OpenPayload(string path, string payload, int kind, long bytes);
}
