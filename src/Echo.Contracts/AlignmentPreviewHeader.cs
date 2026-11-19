namespace Echo.Contracts;

public sealed class AlignmentPreviewHeader
{
    public ReconstructionScene Scene { get; set; } = new();
    public int TotalPoints
    {
        get; set;
    }
    public List<string> Images { get; set; } = [];
}
