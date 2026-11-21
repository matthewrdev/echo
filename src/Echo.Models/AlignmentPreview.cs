namespace Echo.Models;

public sealed class AlignmentPreview
{
    public ReconstructionScene Scene { get; set; } = new();
    public int TotalPoints
    {
        get; set;
    }
}
