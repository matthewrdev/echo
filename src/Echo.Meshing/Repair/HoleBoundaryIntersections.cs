using System.Buffers;
using Echo.Geometry;

namespace Echo.Meshing.Repair;

internal static class HoleBoundaryIntersections
{
    public static bool Crosses(ReadOnlySpan<V3> points, V3 normal, CancellationToken cancellation)
    {
        var axis = Math.Abs(normal.X) < .8 ? new V3(1, 0, 0) : new V3(0, 1, 0);
        var horizontal = normal.Cross(axis).Unit;
        var vertical = normal.Cross(horizontal).Unit;
        var segments = ArrayPool<HoleSegment>.Shared.Rent(points.Length);
        try
        {
            for (var index = 0; index < points.Length; index++)
            {
                var a = points[index] - points[0];
                var b = points[(index + 1) % points.Length] - points[0];
                var ax = a.Dot(horizontal);
                var bx = b.Dot(horizontal);
                var ay = a.Dot(vertical);
                var by = b.Dot(vertical);
                segments[index] = new HoleSegment(index, Math.Min(ax, bx) - 1e-8, Math.Max(ax, bx) + 1e-8,
                    Math.Min(ay, by) - 1e-8, Math.Max(ay, by) + 1e-8);
            }
            Array.Sort(segments, 0, points.Length, Comparer<HoleSegment>.Create((a, b) => a.MinimumX.CompareTo(b.MinimumX)));
            for (var first = 0; first < points.Length; first++)
            {
                cancellation.ThrowIfCancellationRequested();
                var left = segments[first];
                var firstEnd = (left.Index + 1) % points.Length;
                var a = points[left.Index];
                var b = points[firstEnd];
                for (var second = first + 1; second < points.Length; second++)
                {
                    var right = segments[second];
                    if (right.MinimumX > left.MaximumX)
                    {
                        break;
                    }
                    var secondEnd = (right.Index + 1) % points.Length;
                    var adjacent = firstEnd == right.Index || secondEnd == left.Index;
                    var separated = right.MinimumY > left.MaximumY || left.MinimumY > right.MaximumY;
                    if (adjacent || separated)
                    {
                        continue;
                    }
                    var c = points[right.Index];
                    var d = points[secondEnd];
                    var oppositeFirst = (b - a).Cross(c - a).Dot(normal) * (b - a).Cross(d - a).Dot(normal) < -1e-12;
                    var oppositeSecond = (d - c).Cross(a - c).Dot(normal) * (d - c).Cross(b - c).Dot(normal) < -1e-12;
                    if (oppositeFirst && oppositeSecond)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        finally
        {
            ArrayPool<HoleSegment>.Shared.Return(segments);
        }
    }
}
