using Echo.Geometry;

namespace Echo.Texturing;

internal sealed class TextureBrushNode
{
    public V3 Minimum
    {
        get; init;
    }
    public V3 Maximum
    {
        get; init;
    }
    public int Start
    {
        get; init;
    }
    public int Count
    {
        get; init;
    }
    public TextureBrushNode? Left
    {
        get; set;
    }
    public TextureBrushNode? Right
    {
        get; set;
    }
}
