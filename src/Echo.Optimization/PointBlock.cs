namespace Echo.Optimization;

internal sealed class PointBlock
{
    private readonly List<double[]> crossBuffers = [];
    public double[] H { get; } = new double[9];
    public double[] B { get; } = new double[3];
    public Dictionary<int, double[]> Cross { get; } = [];
    public double[] Inverse { get; } = new double[9];

    public void Reset()
    {
        H.AsSpan().Clear();
        B.AsSpan().Clear();
        Cross.Clear();
    }

    public double[] GetCross(int row)
    {
        if (Cross.TryGetValue(row, out var buffer))
        {
            return buffer;
        }

        var slot = Cross.Count;
        if (slot == crossBuffers.Count)
        {
            crossBuffers.Add(new double[3]);
        }

        buffer = crossBuffers[slot];
        buffer.AsSpan().Clear();
        // Reinsert in the current observation order, including after visibility changes.
        Cross.Add(row, buffer);
        return buffer;
    }
}
