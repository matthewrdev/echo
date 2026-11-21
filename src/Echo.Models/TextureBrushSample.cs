namespace Echo.Models;

public sealed class TextureBrushSample
{
    public double[] Centre { get; set; } = [0, 0, 0];
    public double[] Normal { get; set; } = [0, 0, 1];
    public double Radius { get; set; } = 1;
    public double Depth { get; set; } = .25;

    public void Validate()
    {
        var validCentre = Centre is { Length: 3 } && Centre.All(double.IsFinite);
        var validNormal = Normal is { Length: 3 } && Normal.All(double.IsFinite)
            && Math.Abs(Normal.Sum(value => value * value) - 1) < .001;
        var validSize = double.IsFinite(Radius) && double.IsFinite(Depth)
            && Radius > 0 && Depth > 0 && Depth <= Radius;
        if (!validCentre || !validNormal || !validSize)
        {
            throw new EchoException("Texture brush samples need finite coordinates, a unit normal and positive depth no greater than their radius.", 2);
        }
    }
}
