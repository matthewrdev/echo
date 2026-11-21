using Echo.Geometry;
using Echo.Models;
using Echo.Storage.Resources;
using Echo.Storage;

namespace Echo.Meshing.Geometry;

internal sealed class SurfaceTriangleIndex : IDisposable
{
    private readonly SurfaceGeometryReader reader;
    private readonly SurfaceGeometrySnapshot geometry;
    private IList<int> order;
    private int[]? residentOrder;
    private List<SurfaceSearchNode>? residentNodes;
    private IList<SurfaceSearchNode> nodes;
    private readonly SurfaceIndexBuffers buffers;
    private readonly DynamicTriangleIndex additions;

    public SurfaceTriangleIndex(Surface mesh, CancellationToken cancellation, WorkBuffers? work = null)
    {
        reader = new SurfaceGeometryReader(mesh);
        buffers = new SurfaceIndexBuffers(mesh, work, cancellation);
        order = buffers.Order;
        nodes = buffers.Nodes;
        residentOrder = order as int[];
        residentNodes = nodes as List<SurfaceSearchNode>;
        try
        {
            additions = new DynamicTriangleIndex(buffers.Scratch);
            geometry = new SurfaceGeometrySnapshot(mesh, cancellation);
        }
        catch
        {
            buffers.Dispose();
            throw;
        }
        try
        {
            if (order.Count > 0)
            {
                var centres = buffers.RentCentres(order.Count, cancellation);
                try
                {
                    Build(0, order.Count, centres, cancellation);
                }
                finally
                {
                    buffers.ReleaseCentres();
                }
            }
        }
        catch
        {
            geometry.Dispose();
            buffers.Dispose();
            throw;
        }
    }

    public SurfaceTriangle Triangle(int index)
    {
        if (geometry.IsAvailable)
        {
            return geometry.Triangle(index);
        }
        return reader.Triangle(reader.Face(index));
    }

    public void Dispose()
    {
        residentOrder = null;
        residentNodes = null;
        order = [];
        nodes = [];
        geometry.Dispose();
        buffers.Dispose();
    }

    internal IReadOnlyList<SurfaceSearchNode> Nodes => (IReadOnlyList<SurfaceSearchNode>)nodes;
    internal IReadOnlyList<int> Order => (IReadOnlyList<int>)order;

    private int Face(int index) => residentOrder != null ? residentOrder[index] : order[index];
    private SurfaceSearchNode Node(int index) => residentNodes != null ? residentNodes[index] : nodes[index];

    public void Add(SurfaceTriangle triangle) => additions.Add(triangle);

    public bool HasOccluder(V3 start, V3 end)
    {
        return nodes.Count > 0 && HasOccluder(0, start, end);
    }

    private bool HasOccluder(int index, V3 start, V3 end)
    {
        var node = Node(index);
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
            if (SurfaceTriangleIntersection.Crosses(start, end, Triangle(Face(offset))))
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
        var node = Node(index);
        if (DistanceSquared(point, node.Minimum, node.Maximum) >= result.DistanceSquared)
        {
            return;
        }
        if (node.Left >= 0)
        {
            var left = Node(node.Left);
            var right = Node(node.Right);
            var leftFirst = DistanceSquared(point, left.Minimum, left.Maximum) <= DistanceSquared(point, right.Minimum, right.Maximum);
            Nearest(leftFirst ? node.Left : node.Right, point, ref result);
            Nearest(leftFirst ? node.Right : node.Left, point, ref result);
            return;
        }
        for (var offset = node.Start; offset < node.Start + node.Count; offset++)
        {
            var triangle = Triangle(Face(offset));
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
        return additions.Intersects(triangle);
    }

    private bool Intersects(int index, SurfaceTriangle triangle, V3 minimum, V3 maximum)
    {
        var node = Node(index);
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
            if (SurfaceTriangleIntersection.Intersects(triangle, Triangle(Face(offset))))
            {
                return true;
            }
        }
        return false;
    }

    private int Build(int start, int count, IList<double> centres, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var minimum = new V3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        var maximum = minimum * -1;
        for (var index = start; index < start + count; index++)
        {
            if ((index & 4095) == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }
            var triangle = Triangle(Face(index));
            var low = triangle.Minimum;
            var high = triangle.Maximum;
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
            // Resolve each key once. The comparator used to decode two faces
            // and six paged vertex arrays on every comparison at every level.
            var residentCentres = centres as double[];
            for (var index = start; index < start + count; index++)
            {
                if ((index & 4095) == 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                }
                var value = Triangle(Face(index));
                var centre = value.A + value.B + value.C;
                var coordinate = axis == 0 ? centre.X : axis == 1 ? centre.Y : centre.Z;
                if (residentCentres != null)
                {
                    residentCentres[Face(index)] = coordinate;
                }
                else
                {
                    centres[Face(index)] = coordinate;
                }
            }
            SurfaceMedianPartition.Select(order, start, count, centres, cancellation);
            left = Build(start, count / 2, centres, cancellation);
            right = Build(start + count / 2, count - count / 2, centres, cancellation);
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
