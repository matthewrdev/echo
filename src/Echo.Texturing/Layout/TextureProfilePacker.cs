using SkiaSharp;

namespace Echo.Texturing.Layout;

public sealed class TextureProfilePacker : ITextureProfilePacker
{
    private readonly ITextureProfileBuilder profiles;

    public TextureProfilePacker(ITextureProfileBuilder profiles)
    {
        this.profiles = profiles;
    }

    public bool TryPack(IReadOnlyList<TexturePatch> patches, int size, int gutter, double scale, CancellationToken cancellation)
    {
        const int cellSize = 2;
        var columns = size / cellSize;
        var skyline = new int[columns];
        var padding = (int)Math.Ceiling((double)gutter / cellSize) * cellSize;
        foreach (var patch in patches)
        {
            cancellation.ThrowIfCancellationRequested();
            TextureProfile? best = null;
            var bestX = 0;
            var bestY = 0;
            var bestBottom = columns + 1;
            for (var rotation = 0; rotation < 4; rotation++)
            {
                var profile = profiles.Build(patch, scale, cellSize, gutter, rotation);
                if (profile.Height > columns)
                {
                    continue;
                }

                // Four-pixel placement increments retain a conservative two-pixel silhouette.
                for (var x = 0; x + profile.Width <= columns; x += 2)
                {
                    var y = 0;
                    for (var offset = 0; offset < profile.Width; offset++)
                    {
                        if (profile.Bottom[offset] >= 0)
                        {
                            y = Math.Max(y, skyline[x + offset] - profile.Top[offset]);
                        }

                        if (y + profile.Height >= bestBottom)
                        {
                            break;
                        }
                    }

                    if (y + profile.Height < bestBottom)
                    {
                        bestBottom = y + profile.Height;
                        best = profile;
                        bestX = x;
                        bestY = y;
                    }
                }
            }

            if (best == null)
            {
                return false;
            }

            for (var offset = 0; offset < best.Width; offset++)
            {
                if (best.Bottom[offset] >= 0)
                {
                    skyline[bestX + offset] = Math.Max(skyline[bestX + offset], bestY + best.Bottom[offset]);
                }
            }

            patch.QuarterTurns = best.QuarterTurns;
            var rotated = best.QuarterTurns % 2 != 0;
            patch.AtlasBounds = new SKRect(bestX * cellSize + padding, bestY * cellSize + padding,
                (float)(bestX * cellSize + padding + (rotated ? patch.Bounds.Height : patch.Bounds.Width) * scale),
                (float)(bestY * cellSize + padding + (rotated ? patch.Bounds.Width : patch.Bounds.Height) * scale));
        }

        return true;
    }
}
