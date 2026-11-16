namespace Echo.Benchmarks;

internal sealed class SceneStorageComparison
{
    public required string Source
    {
        get; init;
    }
    public required long SourceBytes
    {
        get; init;
    }
    public required long BinaryBytes
    {
        get; init;
    }
    public required long MetadataBytes
    {
        get; init;
    }
    public required int Points
    {
        get; init;
    }
    public required int Cameras
    {
        get; init;
    }
    public required string Note
    {
        get; init;
    }
    public required List<SceneIoMeasurement> Measurements
    {
        get; init;
    }
}
