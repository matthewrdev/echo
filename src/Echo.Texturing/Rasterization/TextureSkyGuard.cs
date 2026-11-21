using Echo.Texturing.Charts;
namespace Echo.Texturing.Rasterization;

// A conservative image heuristic, not semantic sky segmentation. Only smooth,
// sky-coloured regions connected to the top border can move texture samples.
internal sealed class TextureSkyGuard
{
    private readonly int width;
    private readonly int cellSize;
    private readonly int[] nearest;

    internal TextureSkyGuard(int width, int cellSize, int[] nearest)
    {
        this.width = width;
        this.cellSize = cellSize;
        this.nearest = nearest;
    }

    public TextureCoordinate Adjust(double x, double y)
    {
        var column = Math.Clamp((int)(x / cellSize), 0, width - 1);
        var row = Math.Clamp((int)(y / cellSize), 0, nearest.Length / width - 1);
        var index = row * width + column;
        var safe = nearest[index];
        if (safe < 0 || safe == index)
        {
            return new TextureCoordinate(x, y);
        }
        return new TextureCoordinate((safe % width + .5) * cellSize, (safe / width + .5) * cellSize);
    }

}
