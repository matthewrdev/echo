using Echo.Geometry;

namespace Echo.Texturing.Seams;

public sealed class TextureSeamCorrections
{
    public V3[] CornerOffsets { get; init; } = [];
    public int SeamEdges
    {
        get; init;
    }
    public double MeanDifferenceBefore
    {
        get; init;
    }
    public double MeanDifferenceAfter
    {
        get; init;
    }
}
