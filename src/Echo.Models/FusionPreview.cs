namespace Echo.Models;

public sealed class FusionPreview
{
    public FusedVolume Volume { get; set; } = new();
    public int TotalSamples
    {
        get; set;
    }
}
