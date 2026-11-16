using System.Numerics;

namespace Echo.Texturing;

public sealed class TextureAtlasOccupancy
{
    private readonly uint[] words;

    public TextureAtlasOccupancy(int size)
    {
        Size = size;
        WordsPerRow = (size + 31) / 32;
        words = new uint[checked(size * WordsPerRow)];
    }

    public int Size
    {
        get;
    }
    public int WordsPerRow
    {
        get;
    }
    public ReadOnlySpan<uint> Words => words;
    public long Revision
    {
        get; private set;
    }
    public int ChangedFirstRow
    {
        get; private set;
    }
    public int ChangedRowCount
    {
        get; private set;
    }
    public int UsedHeight
    {
        get; private set;
    }

    public bool CanPlace(TextureMask mask, int x, int y)
    {
        var outside = x < 0 || y < 0 || x + mask.Width > Size || y + mask.Height > Size;
        if (outside)
        {
            return false;
        }
        return NextX(mask, x, y) == x;
    }

    public int NextX(TextureMask mask, int x, int y)
    {
        foreach (var span in mask.Spans)
        {
            var end = CollisionEnd(y + span.Y, x + span.Start, x + span.End);
            if (end >= 0)
            {
                // Every intervening x still intersects this occupied run.
                return end - span.Start;
            }
        }
        return x;
    }

    public void Place(TextureMask mask, int x, int y)
    {
        foreach (var span in mask.Spans)
        {
            var start = x + span.Start;
            var end = x + span.End;
            for (var word = start / 32; word <= (end - 1) / 32; word++)
            {
                words[(y + span.Y) * WordsPerRow + word] |= RangeBits(start, end, word);
            }
        }
        UsedHeight = Math.Max(UsedHeight, y + mask.Height);
        ChangedFirstRow = y;
        ChangedRowCount = mask.Height;
        Revision++;
    }

    private int CollisionEnd(int y, int start, int end)
    {
        var row = y * WordsPerRow;
        for (var word = start / 32; word <= (end - 1) / 32; word++)
        {
            var overlap = words[row + word] & RangeBits(start, end, word);
            if (overlap == 0)
            {
                continue;
            }

            var bit = BitOperations.TrailingZeroCount(overlap);
            var empty = ~words[row + word] & (uint.MaxValue << bit);
            while (empty == 0 && word + 1 < WordsPerRow)
            {
                empty = ~words[row + ++word];
            }
            return Math.Min(Size, word * 32 + BitOperations.TrailingZeroCount(empty));
        }
        return -1;
    }

    private uint RangeBits(int start, int end, int word)
    {
        var left = Math.Max(0, start - word * 32);
        var right = Math.Min(32, end - word * 32);
        return (uint.MaxValue << left) & (uint.MaxValue >> (32 - right));
    }
}
