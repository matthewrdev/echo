namespace Echo.Optimization;

internal sealed class PointBlock
{
    public double[] H { get; } = new double[9];
    public double[] B { get; } = new double[3];
    public Dictionary<int, double[]> Cross { get; } = [];
    public double[] Inverse { get; set; } = [];
}
