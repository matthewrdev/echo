using Echo.Compute;
using Echo.Contracts;
using System.ComponentModel.Composition;
using SkiaSharp;

namespace Echo.Texturing;

[Export(typeof(ITextureGapPacker)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class TextureGapPacker : ITextureGapPacker
{
    private readonly ITexturePlacementAccelerator[] accelerators;
    private readonly IComputeService? compute;

    [ImportingConstructor]
    public TextureGapPacker([ImportMany] IEnumerable<ITexturePlacementAccelerator> accelerators, [Import(AllowDefault = true)] IComputeService? compute = null)
    {
        this.accelerators = accelerators.ToArray();
        this.compute = compute;
    }

    public double Improve(IReadOnlyList<TexturePatch> patches, int size, int gutter, double baseline,
        CancellationToken cancellation, Action<string>? progress = null, ComputeBackend backend = ComputeBackend.Auto,
        long maximumCandidates = 64L * 1024 * 1024)
    {
        var budget = new TexturePackingBudget(maximumCandidates);
        if (!Enum.IsDefined(backend))
        {
            throw new ArgumentException("Choose auto, cpu or metal for texture packing.", nameof(backend));
        }
        if (patches.Count < 2 || maximumCandidates == 0)
        {
            return baseline;
        }

        const int cellSize = 2;
        var upper = ScaleLimit(patches, size, gutter, cellSize);
        if (upper <= baseline)
        {
            return baseline;
        }

        var best = Capture(patches);
        var lower = baseline;
        using var session = Open(size / cellSize, backend, cancellation, progress);
        var accelerated = session != null;
        progress?.Invoke($"Filling gaps between intact UV islands with {(session == null ? "CPU" : $"{session.Backend} ({session.Device})")}…");
        try
        {
            for (var iteration = 0; iteration < 8; iteration++)
            {
                cancellation.ThrowIfCancellationRequested();
                var candidate = (lower + upper) * .5;
                var packed = TryPack(patches, size, gutter, candidate, cellSize, session, ref accelerated, cancellation, progress, backend, budget);
                if (budget.Exhausted)
                {
                    progress?.Invoke("Gap search reached its candidate budget; retaining the best complete layout.");
                    break;
                }
                if (packed)
                {
                    lower = candidate;
                    best = Capture(patches);
                }
                else
                {
                    upper = candidate;
                }
                progress?.Invoke($"Gap packing {iteration + 1}/8: texture density {100 * (lower / baseline - 1):F2}% above the skyline layout.");
            }
        }
        finally
        {
            for (var index = 0; index < patches.Count; index++)
            {
                patches[index].AtlasBounds = best[index].Bounds;
                patches[index].QuarterTurns = best[index].QuarterTurns;
            }
        }
        return lower;
    }

    internal bool TryPack(IReadOnlyList<TexturePatch> patches, int size, int gutter, double scale, int cellSize,
        ITexturePlacementSession? session, ref bool accelerated, CancellationToken cancellation, Action<string>? progress = null,
        ComputeBackend backend = ComputeBackend.Auto, TexturePackingBudget? budget = null)
    {
        var atlas = new TextureAtlasOccupancy(size / cellSize);
        var padding = (int)Math.Ceiling((double)gutter / cellSize) * cellSize;
        foreach (var patch in patches)
        {
            cancellation.ThrowIfCancellationRequested();
            var extent = Math.Max(patch.Bounds.Width, patch.Bounds.Height) * scale;
            if (Math.Ceiling(extent / cellSize) + 1 + padding * 2 / cellSize > atlas.Size)
            {
                return false;
            }

            var masks = Enumerable.Range(0, 4)
                .Select(rotation => TextureMaskBuilder.Build(patch, scale, cellSize, gutter, rotation, cancellation)).ToArray();
            if (masks.Any(mask => mask.Spans.Length == 0))
            {
                return false;
            }

            var maximumBottom = Math.Min(atlas.Size, atlas.UsedHeight + masks.Min(mask => mask.Height));
            var placement = Find(atlas, masks, maximumBottom, session, ref accelerated, cancellation, progress, backend, budget);
            if (placement == null)
            {
                return false;
            }

            var mask = masks[placement.QuarterTurns];
            atlas.Place(mask, placement.X, placement.Y);
            patch.QuarterTurns = placement.QuarterTurns;
            var rotated = placement.QuarterTurns % 2 != 0;
            var x = placement.X * cellSize + padding;
            var y = placement.Y * cellSize + padding;
            patch.AtlasBounds = new SKRect(x, y,
                (float)(x + (rotated ? patch.Bounds.Height : patch.Bounds.Width) * scale),
                (float)(y + (rotated ? patch.Bounds.Width : patch.Bounds.Height) * scale));
        }
        return true;
    }

    private TextureMaskPlacement? Find(TextureAtlasOccupancy atlas, TextureMask[] masks, int maximumBottom,
        ITexturePlacementSession? session, ref bool accelerated, CancellationToken cancellation, Action<string>? progress,
        ComputeBackend backend, TexturePackingBudget? budget)
    {
        // Search bottom edges in order. Both backends spend the same candidate
        // budget and stop at the same band, preserving deterministic layouts.
        for (var first = masks.Min(mask => mask.Height); first <= maximumBottom; first += 32)
        {
            cancellation.ThrowIfCancellationRequested();
            var last = Math.Min(first + 31, maximumBottom);
            if (budget != null && !budget.TrySpend((long)atlas.Size * (last - first + 1) * 4))
            {
                return null;
            }
            TextureMaskPlacement? placement;
            if (accelerated && session != null)
            {
                try
                {
                    placement = session.Find(atlas, masks, last, cancellation, first);
                    if (placement != null && !atlas.CanPlace(masks[placement.QuarterTurns], placement.X, placement.Y))
                    {
                        throw new TextureAccelerationException("Metal returned an overlapping placement.");
                    }
                    if (placement != null)
                    {
                        return placement;
                    }
                    continue;
                }
                catch (TextureAccelerationException error) when (backend == ComputeBackend.Auto)
                {
                    accelerated = false;
                    progress?.Invoke($"Metal gap search unavailable; continuing on CPU: {error.Message}");
                }
            }
            placement = TextureMaskSearch.Find(atlas, masks, last, cancellation, first);
            if (placement != null)
            {
                return placement;
            }
        }
        return null;
    }

    private ITexturePlacementSession? Open(int size, ComputeBackend backend, CancellationToken cancellation, Action<string>? progress)
    {
        if (backend == ComputeBackend.Cpu)
        {
            return null;
        }
        foreach (var accelerator in accelerators)
        {
            try
            {
                var session = accelerator.Open(size, cancellation);
                return compute == null ? session : new ObservedTexturePlacementSession(session, compute);
            }
            catch (TextureAccelerationException error) when (backend == ComputeBackend.Auto)
            {
                progress?.Invoke($"Metal gap search unavailable; using CPU: {error.Message}");
            }
        }
        if (backend == ComputeBackend.Metal)
        {
            throw new TextureAccelerationException("Metal packing was requested but no accelerator is available.");
        }
        return null;
    }

    private double ScaleLimit(IReadOnlyList<TexturePatch> patches, int size, int gutter, int cellSize)
    {
        var area = 0.0;
        var largest = 0.0;
        foreach (var patch in patches)
        {
            largest = Math.Max(largest, Math.Max(patch.Bounds.Width, patch.Bounds.Height));
            foreach (var face in patch.Triangles)
            {
                var a = patch.Coordinates[face[0]];
                var b = patch.Coordinates[face[1]];
                var c = patch.Coordinates[face[2]];
                area += Math.Abs((b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])) * .5;
            }
        }
        var available = (size / cellSize - 1 - 2 * (int)Math.Ceiling((double)gutter / cellSize)) * cellSize;
        return Math.Min(available / Math.Max(largest, 1e-12), Math.Sqrt((double)size * size / Math.Max(area, 1e-12)));
    }

    private TexturePatchPlacement[] Capture(IReadOnlyList<TexturePatch> patches)
    {
        return patches.Select(patch => new TexturePatchPlacement(patch.AtlasBounds, patch.QuarterTurns)).ToArray();
    }
}
