namespace Echo.Contracts;

public sealed class ArtifactStorageInventory
{
    public string Token { get; set; } = "";
    public long Bytes
    {
        get; set;
    }
    public int RunCount
    {
        get; set;
    }
    public List<StoredArtifact> Artifacts { get; set; } = [];
}
