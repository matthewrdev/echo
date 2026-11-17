namespace Echo.Meshing;

internal static class SurfaceMedianPartition
{
    public static void Select(int[] order, int start, int count, double[] centres, CancellationToken cancellation)
    {
        var target = start + count / 2;
        var low = start;
        var high = start + count - 1;
        var remaining = 2 * (int)Math.Log2(Math.Max(2, count));
        while (low < high)
        {
            cancellation.ThrowIfCancellationRequested();
            if (remaining-- == 0)
            {
                Array.Sort(order, low, high - low + 1, Comparer<int>.Create((a, b) => Compare(a, b, centres)));
                return;
            }
            var pivot = order[low + (high - low) / 2];
            var left = low;
            var right = high;
            while (left <= right)
            {
                while (Compare(order[left], pivot, centres) < 0)
                {
                    left++;
                }
                while (Compare(order[right], pivot, centres) > 0)
                {
                    right--;
                }
                if (left <= right)
                {
                    var previous = order[left];
                    order[left++] = order[right];
                    order[right--] = previous;
                }
            }
            if (target <= right)
            {
                high = right;
            }
            else if (target >= left)
            {
                low = left;
            }
            else
            {
                return;
            }
        }
    }

    private static int Compare(int first, int second, double[] centres)
    {
        var order = centres[first].CompareTo(centres[second]);
        return order == 0 ? first.CompareTo(second) : order;
    }
}
