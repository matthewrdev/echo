namespace Echo.Models;

public sealed class TexturePriorityStroke
{
    public double Weight { get; set; } = 2;
    public List<TextureBrushSample> Samples { get; set; } = [];

    public void Validate()
    {
        var validWeight = double.IsFinite(Weight) && Weight is >= .25 and <= 4;
        if (!validWeight || Samples == null || Samples.Any(sample => sample == null))
        {
            throw new EchoException("Texture priorities need a weight between 0.25 and 4 and an array of brush samples.", 2);
        }

        foreach (var sample in Samples)
        {
            sample.Validate();
        }
    }
}
