using Echo.Geometry;

namespace Echo.Texturing.Seams;

internal sealed class TextureSeamNode
{
    public int Image
    {
        get; init;
    }
    public int Vertex
    {
        get; init;
    }
    public List<int> Neighbours { get; } = [];
    public V3 Colour
    {
        get; set;
    }
    public double Weight
    {
        get; set;
    }
}
