namespace Echo.Contracts;

public sealed class ExclusionRegion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool Enabled { get; set; } = true;
    public ExclusionShape Shape { get; set; } = ExclusionShape.Box;
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

    public void Validate()
    {
        var validIdentity = !string.IsNullOrWhiteSpace(Id) && Enum.IsDefined(Shape);
        var validCentre = double.IsFinite(CentreX) && double.IsFinite(CentreY) && double.IsFinite(CentreZ);
        var validSize = Positive(Width) && Positive(Height) && Positive(Depth) && Positive(Radius);
        if (!validIdentity || !validCentre || !validSize)
        {
            throw new EchoException("Exclusion regions need an ID, a box or cylinder shape, finite coordinates and positive dimensions.", 2);
        }
    }

    private bool Positive(double value) => double.IsFinite(value) && value > 0;
}
