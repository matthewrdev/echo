namespace Echo.Models;

public sealed class TexturePriorityPainting
{
    public string AlignmentArtifact { get; set; } = "";
    public List<TexturePriorityStroke> Strokes { get; set; } = [];

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AlignmentArtifact) || Strokes == null || Strokes.Any(stroke => stroke == null))
        {
            throw new EchoException("Texture painting needs a saved alignment and an array of strokes.", 2);
        }
        foreach (var stroke in Strokes)
        {
            stroke.Validate();
        }
    }

    public void ValidateAlignment(string alignment)
    {
        if (Strokes.Count > 0 && !string.Equals(AlignmentArtifact, alignment, StringComparison.Ordinal))
        {
            throw new EchoException("Texture painting belongs to a different alignment. Open this run's point cloud and clear or repaint its texture priorities before texturing.", 2);
        }
    }
}
