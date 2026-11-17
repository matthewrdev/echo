namespace Echo.Tests;

public sealed class ReferenceDataset
{
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";
    public string AreaId { get; set; } = "";
    public string MetadataPath { get; set; } = "";
    public string ModelFile { get; set; } = "";
    public string ModelUrl { get; set; } = "";
    public long ModelBytes
    {
        get; set;
    }
    public string ModelSha256 { get; set; } = "";
    public string PolycamCaptureId { get; set; } = "";
    public int PhotoCount
    {
        get; set;
    }
    public string PhotoManifestSha256 { get; set; } = "";
}
