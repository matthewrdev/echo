namespace Echo.Storage;

public interface IArtifactWriteObserver
{
    ArtifactWriteScope BeginScope();
    void Record(string path, string checksum);
}
