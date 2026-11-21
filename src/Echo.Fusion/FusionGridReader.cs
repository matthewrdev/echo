using Echo.Storage.Pages;
using Echo.Storage;

namespace Echo.Fusion;

internal sealed class FusionGridReader
{
    private readonly IList<GridSpan> source;
    private readonly GridAxis axis;
    private readonly GridSpan[] buffer = new GridSpan[4096];
    private int next;
    private int available;
    private int position;
    private GridSpan current;
    private long z = 1;

    public FusionGridReader(IList<GridSpan> source, GridAxis axis)
    {
        this.source = source;
        this.axis = axis;
    }

    public int Read(Span<Grid> destination)
    {
        var count = 0;
        while (count < destination.Length)
        {
            if (z > current.LastZ)
            {
                if (!Next())
                {
                    break;
                }
                z = current.FirstZ;
            }
            var length = (int)Math.Min(destination.Length - count, current.LastZ - z + 1);
            for (var index = 0; index < length; index++)
            {
                destination[count++] = FusionGridAxes.ToWorld(new Grid(current.X, current.Y, (int)z++), axis);
            }
        }
        return count;
    }

    public void Skip(long count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        while (count > 0)
        {
            if (z > current.LastZ)
            {
                if (!Next())
                {
                    throw new InvalidDataException("Fusion grid ranges ended before their declared cell count.");
                }
                z = current.FirstZ;
            }
            var length = Math.Min(count, current.LastZ - z + 1);
            z += length;
            count -= length;
        }
    }

    private bool Next()
    {
        if (position == available)
        {
            position = 0;
            available = Math.Min(buffer.Length, source.Count - next);
            if (available == 0)
            {
                return false;
            }
            if (source is PagedList<GridSpan> paged)
            {
                paged.ReadRange(next, buffer.AsSpan(0, available));
            }
            else
            {
                for (var index = 0; index < available; index++)
                {
                    buffer[index] = source[next + index];
                }
            }
            next += available;
            position = 0;
        }
        current = buffer[position++];
        return true;
    }
}
