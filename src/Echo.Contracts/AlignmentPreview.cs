namespace Echo.Contracts;

public sealed class AlignmentPreview
{
    public ReconstructionScene Scene { get; set; } = new();
    public int TotalPoints
    {
        get; set;
    }
}
