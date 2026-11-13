using System.ComponentModel.Composition;
using Echo.Contracts;
using SkiaSharp;

namespace Echo.Texturing;

[Export(typeof(ITexturePatchPacker)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class TexturePatchPacker : ITexturePatchPacker
{
    private readonly ITextureProfilePacker profiles;

    [ImportingConstructor]
    public TexturePatchPacker(ITextureProfilePacker profiles)
    {
        this.profiles = profiles;
    }

    public double Pack(IReadOnlyList<TexturePatch> patches, int size, int gutter, CancellationToken cancellation = default)
    {
        var ordered = patches.OrderByDescending(p => Math.Max(p.Bounds.Width, p.Bounds.Height))
            .ThenByDescending(p => p.Bounds.Width * p.Bounds.Height).ToArray();
        if (!TryPack(ordered, size, gutter, 0))
        {
            throw new EchoException("Texture atlas is too small for its patches. Increase atlas size or the maximum triangles per chart.", 5);
        }

        var lower = 0.0;
        var upper = Math.Sqrt((double)size * size / ordered.Sum(p => Math.Max(1e-12, p.Bounds.Width * p.Bounds.Height)));
        for (var iteration = 0; iteration < 16; iteration++)
        {
            cancellation.ThrowIfCancellationRequested();
            var candidate = (lower + upper) * .5;
            if (TryPack(ordered, size, gutter, candidate))
            {
                lower = candidate;
            }
            else
            {
                upper = candidate;
            }
        }

        var rectangularScale = lower;
        var silhouetteLower = lower;
        var silhouetteUpper = Math.Sqrt((double)size * size / ordered.Sum(p => Math.Max(1e-12, p.Bounds.Width * p.Bounds.Height))) * 1.5;
        if (profiles.TryPack(ordered, size, gutter, silhouetteLower, cancellation))
        {
            for (var iteration = 0; iteration < 8; iteration++)
            {
                var candidate = (silhouetteLower + silhouetteUpper) * .5;
                if (profiles.TryPack(ordered, size, gutter, candidate, cancellation))
                {
                    silhouetteLower = candidate;
                }
                else
                {
                    silhouetteUpper = candidate;
                }
            }

            profiles.TryPack(ordered, size, gutter, silhouetteLower, cancellation);
            return silhouetteLower;
        }

        TryPack(ordered, size, gutter, rectangularScale);
        return rectangularScale;
    }

    private bool TryPack(IReadOnlyList<TexturePatch> patches, int size, int gutter, double scale)
    {
        var skyline = new List<TextureSkyline> { new(0, 0, size) };
        foreach (var patch in patches)
        {
            var width = Math.Max(1, (int)Math.Ceiling(patch.Bounds.Width * scale)) + gutter * 2;
            var height = Math.Max(1, (int)Math.Ceiling(patch.Bounds.Height * scale)) + gutter * 2;
            TexturePlacement? best = null;
            for (var index = 0; index < skyline.Count; index++)
            {
                best = Choose(skyline, index, width, height, size, false, best);
                if (width != height)
                {
                    best = Choose(skyline, index, height, width, size, true, best);
                }
            }

            if (best == null)
            {
                return false;
            }

            patch.QuarterTurns = best.Rotated ? 1 : 0;
            patch.AtlasBounds = new SKRect(best.X + gutter, best.Y + gutter,
                (float)(best.X + gutter + (best.Rotated ? patch.Bounds.Height : patch.Bounds.Width) * scale),
                (float)(best.Y + gutter + (best.Rotated ? patch.Bounds.Width : patch.Bounds.Height) * scale));
            Insert(skyline, best);
        }

        return true;
    }

    private TexturePlacement? Choose(List<TextureSkyline> skyline, int index, int width, int height,
        int size, bool rotated, TexturePlacement? best)
    {
        var x = skyline[index].X;
        if (x + width > size)
        {
            return best;
        }

        var y = skyline[index].Y;
        for (var next = index; next < skyline.Count && skyline[next].X < x + width; next++)
        {
            y = Math.Max(y, skyline[next].Y);
        }

        var exceedsAtlas = y + height > size;
        var worseHeight = best != null && y + height > best.Y + best.Height;
        var worseTie = best != null && y + height == best.Y + best.Height && width >= best.Width;
        if (exceedsAtlas || worseHeight || worseTie)
        {
            return best;
        }

        return new TexturePlacement(index, x, y, width, height, rotated);
    }

    private void Insert(List<TextureSkyline> skyline, TexturePlacement placement)
    {
        var end = placement.X + placement.Width;
        skyline.Insert(placement.Index, new TextureSkyline(placement.X, placement.Y + placement.Height, placement.Width));
        var index = placement.Index + 1;
        while (index < skyline.Count && skyline[index].X < end)
        {
            var node = skyline[index];
            if (node.X + node.Width <= end)
            {
                skyline.RemoveAt(index);
            }
            else
            {
                skyline[index] = new TextureSkyline(end, node.Y, node.X + node.Width - end);
                break;
            }
        }

        for (index = Math.Max(0, placement.Index - 1); index + 1 < skyline.Count; index++)
        {
            var a = skyline[index];
            var b = skyline[index + 1];
            if (a.Y == b.Y)
            {
                skyline[index] = new TextureSkyline(a.X, a.Y, a.Width + b.Width);
                skyline.RemoveAt(index + 1);
                index--;
            }
        }
    }
}
