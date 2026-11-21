using System.Text.Json.Serialization;

namespace Echo.Models;

public sealed class ExclusionRegion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool Enabled { get; set; } = true;
    public ExclusionShape Shape { get; set; } = ExclusionShape.Box;
    public CropRegionMode Mode { get; set; } = CropRegionMode.Exclude;
    public double RotationY
    {
        get; set;
    }
    public double CentreX
    {
        get; set;
    }
    public double CentreY
    {
        get; set;
    }
    public double CentreZ
    {
        get; set;
    }
    public double Width { get; set; } = 1;
    public double Height { get; set; } = 1;
    public double Depth { get; set; } = 1;
    public double Radius { get; set; } = .5;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CropHull? Hull
    {
        get; set;
    }

    public void Validate()
    {
        var validIdentity = !string.IsNullOrWhiteSpace(Id) && Enum.IsDefined(Shape) && Enum.IsDefined(Mode);
        var validCentre = double.IsFinite(CentreX) && double.IsFinite(CentreY) && double.IsFinite(CentreZ);
        var validRotation = double.IsFinite(RotationY);
        var validSize = Positive(Width) && Positive(Height) && Positive(Depth) && Positive(Radius);
        var validGeometry = validCentre && validRotation && validSize;
        if (!validIdentity || !validGeometry)
        {
            throw new EchoException("Crop regions need an ID, a box, cylinder or painted hull shape, an include or exclude mode, finite coordinates and rotation, and positive dimensions.", 2);
        }
        if (Shape == ExclusionShape.ProjectedHull)
        {
            if (Hull == null)
            {
                throw new EchoException("A painted crop region needs its projected hull.", 2);
            }
            Hull.Validate();
        }
    }

    private bool Positive(double value) => double.IsFinite(value) && value > 0;
}
