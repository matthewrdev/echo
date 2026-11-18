using Echo.Contracts;

namespace Echo.Meshing;

// Immutable depths for one mesh run. Confidence remains backed by the source;
// visibility queries only require depths. The owning context reserves memory.
internal sealed class MeshDepthSamples : IDepthSamples
{
    private readonly DepthMap source;
    private readonly float[] depths;

    public MeshDepthSamples(DepthMap source, CancellationToken cancellation)
    {
        this.source = source;
        depths = new float[source.SampleCount];
        for (var start = 0; start < depths.Length; start += 16384)
        {
            cancellation.ThrowIfCancellationRequested();
            source.ReadDepth(start, depths.AsSpan(start, Math.Min(16384, depths.Length - start)));
        }
    }

    public int Count => depths.Length;
    public float DepthAt(int index) => depths[index];
    public float ConfidenceAt(int index) => source.ConfidenceAt(index);
    public void ReadDepth(int start, Span<float> destination) => depths.AsSpan(start, destination.Length).CopyTo(destination);
}
