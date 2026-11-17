namespace Echo.Tests;

public sealed class ReferencePhotoManifest
{
    public string PolycamCaptureId { get; set; } = "";
    public string ArchiveSha256 { get; set; } = "";
    public List<ReferencePhoto> Photos { get; set; } = [];
}
