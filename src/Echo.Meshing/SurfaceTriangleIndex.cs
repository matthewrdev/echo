using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Meshing;

internal sealed class SurfaceTriangleIndex
{
    private readonly Surface mesh;
    private readonly int[] order;
    private readonly List<SurfaceSearchNode> nodes = [];
    private readonly List<SurfaceTriangle> additions = [];

    public SurfaceTriangleIndex(Surface mesh, CancellationToken cancellation)
    {
        this.mesh = mesh;
        order = Enumerable.Range(0, mesh.Faces.Count).ToArray();
        if (order.Length > 0)
        {
            Build(0, order.Length, cancellation);
        }
    }

    private SurfaceTriangle Triangle(int index)
    {
        var face = mesh.Faces[index];
        return new SurfaceTriangle(new V3(mesh.Vertices[face[0]]), new V3(mesh.Vertices[face[1]]), new V3(mesh.Vertices[face[2]]));
    }

    public void Add(SurfaceTriangle triangle) => additions.Add(triangle);

    public bool HasOccluder(V3 start, V3 end)
    {
        return nodes.Count > 0 && HasOccluder(0, start, end);
    }

    private bool HasOccluder(int index, V3 start, V3 end)
    {
        var node = nodes[index];
        var near = 0.0;
        var far = 1.0;
        var direction = end - start;
        for (var axis = 0; axis < 3; axis++)
        {
            var origin = axis == 0 ? start.X : axis == 1 ? start.Y : start.Z;
            var delta = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;
            var low = axis == 0 ? node.Minimum.X : axis == 1 ? node.Minimum.Y : node.Minimum.Z;
            var high = axis == 0 ? node.Maximum.X : axis == 1 ? node.Maximum.Y : node.Maximum.Z;
            if (Math.Abs(delta) < 1e-12)
            {
                if (origin < low - 1e-8 || origin > high + 1e-8)
                {
                    return false;
                }
                continue;
            }
            var first = (low - origin) / delta;
            var last = (high - origin) / delta;
            near = Math.Max(near, Math.Min(first, last));
            far = Math.Min(far, Math.Max(first, last));
            if (near > far + 1e-8)
            {
                return false;
            }
        }
        if (node.Left >= 0)
        {
            return HasOccluder(node.Left, start, end) || HasOccluder(node.Right, start, end);
        }
        for (var offset = node.Start; offset < node.Start + node.Count; offset++)
        {
            if (SurfaceTriangleIntersection.Crosses(start, end, Triangle(order[offset])))
            {
                return true;
            }
        }
        return false;
    }

    public SurfaceClosestPoint Nearest(V3 point, double maximumDistance)
    {
        var result = new SurfaceClosestPoint(default, default, maximumDistance * maximumDistance);
        if (nodes.Count > 0)
        {
            Nearest(0, point, ref result);
        }
        return result;
    }

    private void Nearest(int index, V3 point, ref SurfaceClosestPoint result)
    {
        var node = nodes[index];
        if (DistanceSquared(point, node.Minimum, node.Maximum) >= result.DistanceSquared)
        {
            return;
        }
        if (node.Left >= 0)
        {
            var left = nodes[node.Left];
            var right = nodes[node.Right];
            var leftFirst = DistanceSquared(point, left.Minimum, left.Maximum) <= DistanceSquared(point, right.Minimum, right.Maximum);
            Nearest(leftFirst ? node.Left : node.Right, point, ref result);
            Nearest(leftFirst ? node.Right : node.Left, point, ref result);
            return;
        }
        for (var offset = node.Start; offset < node.Start + node.Count; offset++)
        {
            var triangle = Triangle(order[offset]);
            var position = SurfaceTriangleIntersection.Closest(point, triangle);
            var delta = point - position;
            var distance = delta.Dot(delta);
            if (distance < result.DistanceSquared)
            {
                result = new SurfaceClosestPoint(position, (triangle.B - triangle.A).Cross(triangle.C - triangle.A).Unit, distance);
            }
        }
    }

    private double DistanceSquared(V3 point, V3 minimum, V3 maximum)
    {
        var delta = point - new V3(Math.Clamp(point.X, minimum.X, maximum.X), Math.Clamp(point.Y, minimum.Y, maximum.Y), Math.Clamp(point.Z, minimum.Z, maximum.Z));
        return delta.Dot(delta);
    }

    public bool Intersects(SurfaceTriangle triangle)
    {
        if (nodes.Count > 0 && Intersects(0, triangle, triangle.Minimum, triangle.Maximum))
        {
            return true;
        }
        foreach (var other in additions)
        {
            if (Overlaps(triangle.Minimum, triangle.Maximum, other.Minimum, other.Maximum)
                && SurfaceTriangleIntersection.Intersects(triangle, other))
            {
                return true;
            }
        }
        return false;
    }

    private bool Intersects(int index, SurfaceTriangle triangle, V3 minimum, V3 maximum)
    {
        var node = nodes[index];
        if (!Overlaps(minimum, maximum, node.Minimum, node.Maximum))
        {
            return false;
        }
        if (node.Left >= 0)
        {
            return Intersects(node.Left, triangle, minimum, maximum) || Intersects(node.Right, triangle, minimum, maximum);
        }
        for (var offset = node.Start; offset < node.Start + node.Count; offset++)
        {
            if (SurfaceTriangleIntersection.Intersects(triangle, Triangle(order[offset])))
            {
                return true;
            }
        }
        return false;
    }

    private int Build(int start, int count, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var minimum = new V3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        var maximum = minimum * -1;
        for (var index = start; index < start + count; index++)
        {
            var low = Triangle(order[index]).Minimum;
            var high = Triangle(order[index]).Maximum;
            minimum = new V3(Math.Min(minimum.X, low.X), Math.Min(minimum.Y, low.Y), Math.Min(minimum.Z, low.Z));
            maximum = new V3(Math.Max(maximum.X, high.X), Math.Max(maximum.Y, high.Y), Math.Max(maximum.Z, high.Z));
        }
        var slot = nodes.Count;
        nodes.Add(default);
        var left = -1;
        var right = -1;
        if (count > 12)
        {
            var extent = maximum - minimum;
            var axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
            double Centre(int triangle)
            {
                var value = Triangle(triangle);
                var centre = value.A + value.B + value.C;
                return axis == 0 ? centre.X : axis == 1 ? centre.Y : centre.Z;
            }
            Array.Sort(order, start, count, Comparer<int>.Create((a, b) => Centre(a).CompareTo(Centre(b))));
            left = Build(start, count / 2, cancellation);
            right = Build(start + count / 2, count - count / 2, cancellation);
        }
        nodes[slot] = new SurfaceSearchNode(minimum, maximum, start, count, left, right);
        return slot;
    }

    private bool Overlaps(V3 lowA, V3 highA, V3 lowB, V3 highB)
    {
        var x = lowA.X <= highB.X + 1e-8 && highA.X >= lowB.X - 1e-8;
        var y = lowA.Y <= highB.Y + 1e-8 && highA.Y >= lowB.Y - 1e-8;
        var z = lowA.Z <= highB.Z + 1e-8 && highA.Z >= lowB.Z - 1e-8;
        return x && y && z;
    }
}
