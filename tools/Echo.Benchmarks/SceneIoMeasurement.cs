namespace Echo.Benchmarks;

public sealed class SceneIoMeasurement
{
    public string Operation { get; set; } = "";
    public int Iteration
    {
        get; set;
    }
    public double Milliseconds
    {
        get; set;
    }
    public long AllocatedBytes
    {
        get; set;
    }
}
