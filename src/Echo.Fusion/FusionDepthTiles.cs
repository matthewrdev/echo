using System.Buffers;
using Echo.Geometry;

namespace Echo.Fusion;

internal sealed class FusionDepthTiles : IDisposable
{
    private float[]? depths;
    private readonly int capacity;
    private readonly CancellationToken cancellation;
    private IEnumerator<FusionDepthWindow>? pending;
    private bool hasPending;
    private bool disposed;

    public FusionDepthTiles(int capacity, CancellationToken cancellation)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 4);
        this.capacity = capacity;
        this.cancellation = cancellation;
    }

    public FusionDepthWindow[] Windows { get; } = new FusionDepthWindow[128];
    public float[] Depths => depths ??= ArrayPool<float>.Shared.Rent(capacity);
    public int Count
    {
        get; private set;
    }
    public int Length
    {
        get; private set;
    }

    public void Start(IReadOnlyList<DepthView> views, FusionBounds bounds, V3 origin)
    {
        pending?.Dispose();
        pending = Regions(views, bounds, origin).GetEnumerator();
        hasPending = pending.MoveNext();
    }

    public bool LoadNext()
    {
        Count = 0;
        Length = 0;
        while (hasPending && Count < Windows.Length)
        {
            cancellation.ThrowIfCancellationRequested();
            var window = pending!.Current;
            if (window.Region.Count > capacity - Length)
            {
                break;
            }

            Windows[Count++] = window with
            {
                Offset = Length
            };
            var region = window.Region;
            var depths = Depths;
            if (region.Width == window.View.Map.Width)
            {
                window.View.Map.ReadDepth(region.Top * region.Width, depths.AsSpan(Length, region.Count));
            }
            else
            {
                for (var row = 0; row < region.Height; row++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    window.View.Map.ReadDepth((region.Top + row) * window.View.Map.Width + region.Left,
                        depths.AsSpan(Length + row * region.Width, region.Width));
                }
            }

            Length += region.Count;
            hasPending = pending.MoveNext();
        }

        return Count > 0;
    }

    private IEnumerable<FusionDepthWindow> Regions(IReadOnlyList<DepthView> views, FusionBounds bounds, V3 origin)
    {
        foreach (var view in views)
        {
            cancellation.ThrowIfCancellationRequested();
            var region = FusionDepthProjection.Project(view, bounds, origin);
            if (region.Width < 2 || region.Height < 2)
            {
                continue;
            }

            var width = Math.Min(region.Width, capacity / 2);
            var height = Math.Min(region.Height, capacity / width);
            // Adjacent tiles share only the pixel edge. Their bilinear base-pixel
            // ranges are disjoint, so each camera contributes once per voxel.
            for (var top = region.Top; top < region.Top + region.Height - 1; top += height - 1)
            {
                for (var left = region.Left; left < region.Left + region.Width - 1; left += width - 1)
                {
                    yield return new FusionDepthWindow(view, new FusionDepthRegion(left, top,
                        Math.Min(width, region.Left + region.Width - left), Math.Min(height, region.Top + region.Height - top)), 0);
                }
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        pending?.Dispose();
        if (depths != null)
        {
            ArrayPool<float>.Shared.Return(depths);
        }
    }
}
