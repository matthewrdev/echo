namespace Echo.Contracts;

public sealed class Feature
{
    public string Kind { get; set; } = "corner";
    public double X
    {
        get; set;
    }
    public double Y
    {
        get; set;
    }
    public float[] Descriptor { get; set; } = [];
}
