using Echo.Contracts;
using SkiaSharp;

namespace Echo.Texturing;

public sealed class TexturePatchPacker : ITexturePatchPacker
{
    private readonly ITextureProfilePacker profiles;
    private readonly ITextureGapPacker gaps;

    public TexturePatchPacker(ITextureProfilePacker profiles, ITextureGapPacker gaps)
    {
        this.profiles = profiles;
        this.gaps = gaps;
    }

    public double Pack(IReadOnlyList<TexturePatch> patches, int size, int gutter, CancellationToken cancellation = default,
        Action<string>? progress = null, ComputeBackend backend = ComputeBackend.Auto, Action<OperationProgress>? operationProgress = null)
    {
        return TryPack(patches, size, gutter, cancellation, progress, backend, operationProgress)
            ?? throw new EchoException("Texture atlas is too small for its patches. Increase atlas size or the maximum triangles per chart.", 5);
    }

    public double? TryPack(IReadOnlyList<TexturePatch> patches, int size, int gutter, CancellationToken cancellation = default,
        Action<string>? progress = null, ComputeBackend backend = ComputeBackend.Auto, Action<OperationProgress>? operationProgress = null)
    {
        cancellation.ThrowIfCancellationRequested();
        if (patches.Count == 0)
        {
            return 0;
        }
        var ordered = patches.OrderByDescending(p => Math.Max(p.Bounds.Width, p.Bounds.Height))
            .ThenByDescending(p => p.Bounds.Width * p.Bounds.Height).ToArray();
        var baseline = TryPackSkyline(ordered, size, gutter, cancellation, operationProgress);
        operationProgress?.Invoke(PackingProgress(2, "Filling gaps between intact UV islands…"));
        return baseline.HasValue ? gaps.Improve(ordered, size, gutter, baseline.Value, cancellation, progress, backend) : null;
    }

    internal double? TryPackSkyline(IReadOnlyList<TexturePatch> ordered, int size, int gutter, CancellationToken cancellation, Action<OperationProgress>? operationProgress = null)
    {
        operationProgress?.Invoke(PackingProgress(0, "Packing rectangular chart bounds…", 0, 16));
        if (!TryPack(ordered, size, gutter, 0))
        {
            return null;
        }

        double area = 0;
        foreach (var patch in ordered)
        {
            area += Math.Max(1e-12, patch.Bounds.Width * patch.Bounds.Height);
        }
        var lower = 0.0;
        var upper = Math.Sqrt((double)size * size / area);
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
            operationProgress?.Invoke(PackingProgress(0, "Packing rectangular chart bounds…", iteration + 1, 16));
        }

        var rectangularScale = lower;
        var silhouetteLower = lower;
        var silhouetteUpper = Math.Sqrt((double)size * size / area) * 1.5;
        operationProgress?.Invoke(PackingProgress(1, "Packing chart silhouettes…", 0, 8));
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
                operationProgress?.Invoke(PackingProgress(1, "Packing chart silhouettes…", iteration + 1, 8));
            }

            profiles.TryPack(ordered, size, gutter, silhouetteLower, cancellation);
            return silhouetteLower;
        }

        TryPack(ordered, size, gutter, rectangularScale);
        return rectangularScale;
    }

    private OperationProgress PackingProgress(int index, string message, long? completed = null, long? total = null)
    {
        return new OperationProgress("Atlas packing", message, completed, total, "iterations")
        {
            Steps = ["Rectangular packing", "Silhouette packing", "Gap packing"],
            StepIndex = index
        };
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
