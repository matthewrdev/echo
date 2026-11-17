using System.Runtime.InteropServices;

namespace Echo.Meshing;

internal static class FaceViewCandidateRanking
{
    public static void RetainBest(List<FaceViewCandidate> candidates, int limit)
    {
        var values = CollectionsMarshal.AsSpan(candidates);
        var low = 0;
        var high = values.Length - 1;
        var remaining = 2 * (int)Math.Log2(Math.Max(2, values.Length));
        while (values.Length > limit && low < high)
        {
            if (remaining-- == 0)
            {
                values[low..(high + 1)].Sort();
                break;
            }
            var pivot = values[low + (high - low) / 2];
            var left = low;
            var right = high;
            while (left <= right)
            {
                while (values[left].CompareTo(pivot) < 0)
                {
                    left++;
                }
                while (values[right].CompareTo(pivot) > 0)
                {
                    right--;
                }
                if (left <= right)
                {
                    var previous = values[left];
                    values[left++] = values[right];
                    values[right--] = previous;
                }
            }
            if (limit - 1 <= right)
            {
                high = right;
            }
            else if (limit - 1 >= left)
            {
                low = left;
            }
            else
            {
                break;
            }
        }
        values[..Math.Min(limit, values.Length)].Sort();
        if (candidates.Count > limit)
        {
            candidates.RemoveRange(limit, candidates.Count - limit);
        }
    }
}
