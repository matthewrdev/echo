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

    public ref BundleCrossTerm GetCross(int row)
    {
        ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(crossIndices, row, out var exists);
        if (!exists)
        {
            slot = crossIndices.Count - 1;
            var term = new BundleCrossTerm(row);
            if (slot == crossBuffers.Count)
            {
                crossBuffers.Add(term);
            }
            else
            {
                crossBuffers[slot] = term;
            }
        }

        return ref CollectionsMarshal.AsSpan(crossBuffers)[slot];
    }
}
