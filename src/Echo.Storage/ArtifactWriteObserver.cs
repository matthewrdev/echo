
namespace Echo.Storage;

public sealed class ArtifactWriteObserver : IArtifactWriteObserver
{
    private readonly AsyncLocal<ArtifactWriteScope?> current = new();

    public ArtifactWriteObserver()
    {
    }

    public ArtifactWriteScope BeginScope()
    {
        var previous = current.Value;
        var scope = new ArtifactWriteScope(() => current.Value = previous);
        current.Value = scope;
        return scope;
    }

    public void Record(string path, string checksum) => current.Value?.Record(path, checksum);
}
