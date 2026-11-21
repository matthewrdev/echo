using Echo.Geometry;
using Echo.Meshing.Geometry;
using Echo.Storage.Pages;
using Echo.Storage;

namespace Echo.Meshing.Repair;

internal static class PlanarEarTriangulation
{
    public static List<int[]>? Fill(List<int> boundary, ReadOnlySpan<V3> source, V3 normal,
        BinaryHashIndex<SurfaceEdge, BoundaryEdge> edges, CancellationToken cancellation)
    {
        if (HoleBoundaryIntersections.Crosses(source, normal, cancellation))
        {
            return null;
        }
        var points = source.ToArray();
        var count = points.Length;
        var before = new int[count];
        var after = new int[count];
        var active = new bool[count];
        var watched = new int[count];
        var watchHead = new int[count];
        var watchNext = new int[count];
        var watchPrevious = new int[count];
        Array.Fill(watched, -1);
        Array.Fill(watchHead, -1);
        Array.Fill(watchNext, -1);
        Array.Fill(watchPrevious, -1);
        for (var index = 0; index < count; index++)
        {
            before[index] = (index + count - 1) % count;
            after[index] = (index + 1) % count;
            active[index] = true;
        }
        // Preserve the original first-valid-ear ordering. An unchanged ear
        // only needs rechecking when its blocking vertex disappears.
        var ears = new SortedSet<int>();
        var searchStarts = new int[count];
        void Forget(int index)
        {
            var vertex = watched[index];
            if (vertex < 0)
            {
                return;
            }
            if (watchPrevious[index] < 0)
            {
                watchHead[vertex] = watchNext[index];
            }
            else
            {
                watchNext[watchPrevious[index]] = watchNext[index];
            }
            if (watchNext[index] >= 0)
            {
                watchPrevious[watchNext[index]] = watchPrevious[index];
            }
            watched[index] = -1;
        }
        bool Evaluate(int index, int searchStart)
        {
            cancellation.ThrowIfCancellationRequested();
            Forget(index);
            var first = before[index];
            var last = after[index];
            var a = points[first];
            var b = points[index];
            var c = points[last];
            var diagonal = new SurfaceEdge(Math.Min(boundary[first], boundary[last]), Math.Max(boundary[first], boundary[last]));
            if ((b - a).Cross(c - a).Dot(normal) <= 1e-12 || edges.ContainsKey(diagonal))
            {
                return false;
            }
            for (var vertex = searchStart; vertex < count; vertex++)
            {
                var corner = vertex == first || vertex == index || vertex == last;
                if (!active[vertex] || corner)
                {
                    continue;
                }
                var point = points[vertex];
                var inside = (b - a).Cross(point - a).Dot(normal) >= -1e-12
                    && (c - b).Cross(point - b).Dot(normal) >= -1e-12
                    && (a - c).Cross(point - c).Dot(normal) >= -1e-12;
                if (inside)
                {
                    watched[index] = vertex;
                    watchPrevious[index] = -1;
                    watchNext[index] = watchHead[vertex];
                    if (watchHead[vertex] >= 0)
                    {
                        watchPrevious[watchHead[vertex]] = index;
                    }
                    watchHead[vertex] = index;
                    return false;
                }
            }
            return true;
        }
        for (var index = 0; index < count; index++)
        {
            ears.Add(index);
        }
        var result = new List<int[]>(count - 2);
        var remaining = count;
        while (remaining > 3)
        {
            cancellation.ThrowIfCancellationRequested();
            if (ears.Count == 0)
            {
                return null;
            }
            var index = ears.Min;
            ears.Remove(index);
            if (!Evaluate(index, searchStarts[index]))
            {
                continue;
            }
            var first = before[index];
            var last = after[index];
            result.Add([boundary[first], boundary[index], boundary[last]]);
            ears.Remove(index);
            Forget(index);
            active[index] = false;
            after[first] = last;
            before[last] = first;
            foreach (var neighbour in (ReadOnlySpan<int>)[first, last])
            {
                Forget(neighbour);
                searchStarts[neighbour] = 0;
                ears.Add(neighbour);
            }
            while (watchHead[index] >= 0)
            {
                var blocked = watchHead[index];
                Forget(blocked);
                searchStarts[blocked] = index + 1;
                ears.Add(blocked);
            }
            remaining--;
        }
        var start = Array.FindIndex(active, value => value);
        var middle = after[start];
        var end = after[middle];
        if ((points[middle] - points[start]).Cross(points[end] - points[start]).Dot(normal) <= 1e-12)
        {
            return null;
        }
        result.Add([boundary[start], boundary[middle], boundary[end]]);
        return result;
    }
}
