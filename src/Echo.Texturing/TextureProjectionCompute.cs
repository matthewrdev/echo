using System.Buffers;
using Echo.Compute;
using Echo.Geometry;
using SkiaSharp;

namespace Echo.Texturing;

internal sealed class TextureProjectionCompute
{
    private readonly IComputeSession session;
    private readonly int size;
    private readonly int tilesWide;
    private readonly float[] triangles = new float[1024 * 32];
    private readonly int[] offsets;
    private readonly int[] cursors;
    private readonly int[] active;
    private int count;
    private int width;
    private int height;
    private bool swap;

    public TextureProjectionCompute(IComputeSession session, int size, ReadOnlySpan<SKColor> target)
    {
        this.session = session;
        this.size = size;
        tilesWide = (size + 15) / 16;
        offsets = new int[tilesWide * tilesWide + 1];
        cursors = new int[offsets.Length - 1];
        active = new int[cursors.Length];
        session.Upload<SKColor>(1, target);
    }

    public void Source(ReadOnlySpan<SKColor> pixels, int width, int height, bool swap)
    {
        Flush();
        this.width = width;
        this.height = height;
        this.swap = swap;
        session.Upload<SKColor>(0, pixels);
    }

    public void Add(ReadOnlySpan<TextureCoordinate> uv, ReadOnlySpan<V3> projected, ReadOnlySpan<V3> corrections)
    {
        var triangle = triangles.AsSpan(count * 32, 32);
        for (var corner = 0; corner < 3; corner++)
        {
            triangle[corner * 2] = (float)(uv[corner].X * size);
            triangle[corner * 2 + 1] = (float)(uv[corner].Y * size);
            triangle[6 + corner * 3] = (float)projected[corner].X;
            triangle[7 + corner * 3] = (float)projected[corner].Y;
            triangle[8 + corner * 3] = (float)projected[corner].Z;
            triangle[15 + corner * 3] = (float)corrections[corner].X;
            triangle[16 + corner * 3] = (float)corrections[corner].Y;
            triangle[17 + corner * 3] = (float)corrections[corner].Z;
        }
        if (++count == 1024)
        {
            Flush();
        }
    }

    public void Read(Span<SKColor> target)
    {
        Flush();
        session.Read<SKColor>(1, target);
    }

    private void Flush()
    {
        if (count == 0)
        {
            return;
        }
        Array.Clear(offsets);
        for (var i = 0; i < count; i++)
        {
            var bounds = Bounds(i);
            for (var y = bounds.Top; y <= bounds.Bottom; y++)
            {
                for (var x = bounds.Left; x <= bounds.Right; x++)
                {
                    offsets[y * tilesWide + x + 1]++;
                }
            }
        }
        var activeCount = 0;
        for (var i = 0; i < cursors.Length; i++)
        {
            if (offsets[i + 1] > 0)
            {
                active[activeCount++] = i;
            }
            offsets[i + 1] = checked(offsets[i + 1] + offsets[i]);
            cursors[i] = offsets[i];
        }
        if (offsets[^1] > 16 * 1024 * 1024)
        {
            throw new ComputeException("Texture triangle bins exceed the bounded compute batch.");
        }
        var indices = ArrayPool<int>.Shared.Rent(offsets[^1]);
        try
        {
            for (var i = 0; i < count; i++)
            {
                var bounds = Bounds(i);
                for (var y = bounds.Top; y <= bounds.Bottom; y++)
                {
                    for (var x = bounds.Left; x <= bounds.Right; x++)
                    {
                        indices[cursors[y * tilesWide + x]++] = i;
                    }
                }
            }
            session.Upload<float>(2, triangles.AsSpan(0, count * 32));
            session.Upload<int>(3, offsets);
            session.Upload<int>(4, indices.AsSpan(0, offsets[^1]));
            session.Upload<int>(5, active.AsSpan(0, activeCount));
            if (activeCount > 0)
            {
                session.Dispatch(ComputeKernel.TextureProject, activeCount * 256, [size, width, height, swap ? 1 : 0]);
            }
            count = 0;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(indices);
        }
    }

    private SKRectI Bounds(int index)
    {
        var t = triangles.AsSpan(index * 32, 6);
        var left = Math.Clamp((int)MathF.Floor(MathF.Min(t[0], MathF.Min(t[2], t[4]))) / 16, 0, tilesWide - 1);
        var right = Math.Clamp((int)MathF.Ceiling(MathF.Max(t[0], MathF.Max(t[2], t[4]))) / 16, 0, tilesWide - 1);
        var top = Math.Clamp((int)MathF.Floor(MathF.Min(t[1], MathF.Min(t[3], t[5]))) / 16, 0, tilesWide - 1);
        var bottom = Math.Clamp((int)MathF.Ceiling(MathF.Max(t[1], MathF.Max(t[3], t[5]))) / 16, 0, tilesWide - 1);
        return new SKRectI(left, top, right, bottom);
    }
}
