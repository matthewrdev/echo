using Echo.Texturing.Charts;
using SkiaSharp;

namespace Echo.Texturing.Layout;

public sealed class TexturePatch
{
    public List<int[]> Triangles { get; } = [];
    public List<int> Faces { get; } = [];
    public Dictionary<int, double[]> Coordinates { get; } = [];
    public int QuarterTurns
    {
        get; set;
    }
    public SKRect Bounds
    {
        get; set;
    }
    public SKRect AtlasBounds
    {
        get; set;
    }

    public TextureCoordinate Coordinate(int vertex, int quarterTurns)
    {
        var point = Coordinates[vertex];
        var x = point[0] - Bounds.Left;
        var y = point[1] - Bounds.Top;
        return quarterTurns switch
        {
            1 => new TextureCoordinate(Bounds.Height - y, x),
            2 => new TextureCoordinate(Bounds.Width - x, Bounds.Height - y),
            3 => new TextureCoordinate(y, Bounds.Width - x),
            _ => new TextureCoordinate(x, y)
        };
    }
}
