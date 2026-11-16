namespace Echo.Texturing;

public static class TextureMaskSearch
{
    public static TextureMaskPlacement? Find(TextureAtlasOccupancy atlas, IReadOnlyList<TextureMask> masks,
        int maximumBottom, CancellationToken cancellation, int minimumBottom = 1)
    {
        TextureMaskPlacement? best = null;
        var bestBottom = Math.Min(atlas.Size, maximumBottom) + 1;
        foreach (var mask in masks)
        {
            for (var y = Math.Max(0, minimumBottom - mask.Height); y + mask.Height < bestBottom; y++)
            {
                cancellation.ThrowIfCancellationRequested();
                for (var x = 0; x + mask.Width <= atlas.Size;)
                {
                    var next = atlas.NextX(mask, x, y);
                    if (next == x)
                    {
                        best = new TextureMaskPlacement(x, y, mask.QuarterTurns);
                        bestBottom = y + mask.Height;
                        break;
                    }
                    x = next;
                }
            }
        }
        return best;
    }
}
