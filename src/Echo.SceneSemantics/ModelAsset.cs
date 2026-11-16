namespace Echo.SceneSemantics;

public sealed record ModelAsset
{
    public required string Name
    {
        get; init;
    }
    public required string FileName
    {
        get; init;
    }
    public required string Fingerprint
    {
        get; init;
    }
    public required Uri DownloadUri
    {
        get; init;
    }
    public required string DownloadSize
    {
        get; init;
    }
    public required long MaximumBytes
    {
        get; init;
    }
}
