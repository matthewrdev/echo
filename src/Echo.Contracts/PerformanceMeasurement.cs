namespace Echo.Contracts;

public sealed record PerformanceMeasurement
{
    public string Stage { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public long Count
    {
        get; set;
    }
    public double Total
    {
        get; set;
    }
    public double Minimum
    {
        get; set;
    }
    public double Maximum
    {
        get; set;
    }
}
