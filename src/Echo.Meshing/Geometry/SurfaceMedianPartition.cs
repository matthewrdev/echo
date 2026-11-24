namespace Echo.Meshing.Geometry;

internal static class SurfaceMedianPartition
{
    public static void Select(IList<int> order, int start, int count, IList<double> centres, CancellationToken cancellation)
    {
        if (order is int[] residentOrder && centres is double[] residentCentres)
        {
            SelectResident(residentOrder, start, count, residentCentres, cancellation);
            return;
        }

        var target = start + count / 2;
        var low = start;
        var high = start + count - 1;
        var remaining = 2 * (int)Math.Log2(Math.Max(2, count));
        while (low < high)
        {
            cancellation.ThrowIfCancellationRequested();
            if (remaining-- == 0)
            {
                HeapSort(order, low, high - low + 1, centres, cancellation);

                return;
            }

            var pivot = low + (high - low) / 2;
            var pivotFace = order[pivot];
            var pivotCentre = centres[pivot];
            var left = low;
            var right = high;
            while (left <= right)
            {
                while (Compare(centres[left], order[left], pivotCentre, pivotFace) < 0)
                {
                    left++;
                }

                while (Compare(centres[right], order[right], pivotCentre, pivotFace) > 0)
                {
                    right--;
                }

                if (left <= right)
                {
                    Swap(order, centres, left++, right--);
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

    private static void HeapSort(IList<int> order, int start, int count, IList<double> centres, CancellationToken cancellation)
    {
        for (var root = count / 2 - 1; root >= 0; root--)
        {
            Sift(order, start, count, root, centres, cancellation);
        }

        for (var end = count - 1; end > 0; end--)
        {
            Swap(order, centres, start, start + end);
            Sift(order, start, end, 0, centres, cancellation);
        }
    }

    private static void Sift(IList<int> order, int start, int count, int root, IList<double> centres, CancellationToken cancellation)
    {
        while (root < count / 2)
        {
            cancellation.ThrowIfCancellationRequested();
            var child = root * 2 + 1;
            if (child + 1 < count && Compare(centres[start + child], order[start + child], centres[start + child + 1], order[start + child + 1]) < 0)
            {
                child++;
            }

            if (Compare(centres[start + root], order[start + root], centres[start + child], order[start + child]) >= 0)
            {
                return;
            }

            Swap(order, centres, start + root, start + child);
            root = child;
        }
    }

    private static void Swap(IList<int> order, IList<double> centres, int first, int second)
    {
        if (first == second)
        {
            return;
        }

        var face = order[first];
        var centre = centres[first];
        order[first] = order[second];
        centres[first] = centres[second];
        order[second] = face;
        centres[second] = centre;
    }

    private static int Compare(double firstCentre, int firstFace, double secondCentre, int secondFace)
    {
        var order = firstCentre.CompareTo(secondCentre);
        return order == 0 ? firstFace.CompareTo(secondFace) : order;
    }

    private static void SelectResident(int[] order, int start, int count, double[] centres, CancellationToken cancellation)
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
                Array.Sort(order, low, high - low + 1, Comparer<int>.Create((a, b) => CompareResident(a, b, centres)));
                return;
            }

            var pivot = order[low + (high - low) / 2];
            var left = low;
            var right = high;
            while (left <= right)
            {
                while (CompareResident(order[left], pivot, centres) < 0)
                {
                    left++;
                }

                while (CompareResident(order[right], pivot, centres) > 0)
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

    private static int CompareResident(int first, int second, double[] centres)
    {
        var order = centres[first].CompareTo(centres[second]);
        return order == 0 ? first.CompareTo(second) : order;
    }
}
