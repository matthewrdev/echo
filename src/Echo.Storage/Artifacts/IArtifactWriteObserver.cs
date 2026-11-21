namespace Echo.Storage.Artifacts;

public interface IArtifactWriteObserver
{
    ArtifactWriteScope BeginScope();
    void Record(string path, string checksum);
}
