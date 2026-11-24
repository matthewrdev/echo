using System.Runtime.InteropServices;

namespace Echo.Optimization;

internal sealed class PointBlock
{
    private readonly Dictionary<int, int> crossIndices = [];
    private readonly List<BundleCrossTerm> crossBuffers = [];
    public double[] H { get; } = new double[9];
    public double[] B { get; } = new double[3];
    public ReadOnlySpan<BundleCrossTerm> Cross => CollectionsMarshal.AsSpan(crossBuffers)[..crossIndices.Count];
    public double[] Inverse { get; } = new double[9];

    public void Reset()
    {
        H.AsSpan().Clear();
        B.AsSpan().Clear();
        crossIndices.Clear();
    }

    public double[] GetCross(int row)
    {
        if (crossIndices.TryGetValue(row, out var existing))
        {
            return crossBuffers[existing].Value;
        }

        var slot = crossIndices.Count;
        if (slot == crossBuffers.Count)
        {
            crossBuffers.Add(new BundleCrossTerm(row, new double[3]));
        }
        else
        {
            var buffer = crossBuffers[slot].Value;
            buffer.AsSpan().Clear();
            crossBuffers[slot] = new BundleCrossTerm(row, buffer);
        }

        // The contiguous view follows the current observation order.
        crossIndices.Add(row, slot);
        return crossBuffers[slot].Value;
    }
}
