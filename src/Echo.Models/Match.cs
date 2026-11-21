namespace Echo.Models;

public sealed class Match
{
    public bool? GeometricInlier
    {
        get; set;
    }
    public string Kind { get; set; } = "corner";
    public double[] A { get; set; } = [];
    public double[] B { get; set; } = [];
    public double Score
    {
        get; set;
    }
    public bool Manual
    {
        get; set;
    }
}
