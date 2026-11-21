namespace Echo.Models;

public sealed class LineMatch
{
    public int IndexA
    {
        get; set;
    }
    public int IndexB
    {
        get; set;
    }
    public double[] StartA { get; set; } = [];
    public double[] EndA { get; set; } = [];
    public double[] StartB { get; set; } = [];
    public double[] EndB { get; set; } = [];
    public double Score
    {
        get; set;
    }
}
