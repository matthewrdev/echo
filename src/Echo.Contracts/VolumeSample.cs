namespace Echo.Contracts;

public record struct VolumeSample
{
    public int X
    {
        get; set;
    }
    public int Y
    {
        get; set;
    }
    public int Z
    {
        get; set;
    }
    public float Distance
    {
        get; set;
    }
    public int Views
    {
        get; set;
    }
}
