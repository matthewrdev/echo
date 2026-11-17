using Echo.Geometry;
using Echo.Storage;

namespace Echo.Meshing;

// Balanced incremental bounds avoid scanning every previously inserted patch.
internal sealed class DynamicTriangleIndex
{
    private readonly IList<DynamicTriangleNode> nodes;
    private int root = -1;

    public DynamicTriangleIndex(WorkBuffers? work = null)
    {
        nodes = work?.List(new DynamicTriangleNodeCodec()) ?? new List<DynamicTriangleNode>();
    }

    public void Add(SurfaceTriangle triangle)
    {
        var index = nodes.Count;
        nodes.Add(new DynamicTriangleNode(triangle, triangle.Minimum, triangle.Maximum, -1, -1, 1));
        root = Insert(root, index);
    }

    private int Insert(int index, int added)
    {
        if (index < 0)
        {
            return added;
        }
        var node = nodes[index];
        var triangle = nodes[added].Triangle;
        var first = triangle.A.X / 3 + triangle.B.X / 3 + triangle.C.X / 3;
        var second = node.Triangle.A.X / 3 + node.Triangle.B.X / 3 + node.Triangle.C.X / 3;
        node = first < second ? node with
        {
            Left = Insert(node.Left, added)
        } : node with
        {
            Right = Insert(node.Right, added)
        };
        Update(index, node);
        var balance = Height(node.Left) - Height(node.Right);
        if (balance > 1)
        {
            var left = nodes[node.Left];
            if (Height(left.Left) < Height(left.Right))
            {
                Update(index, nodes[index] with
                {
                    Left = RotateLeft(node.Left)
                });
            }
            return RotateRight(index);
        }
        if (balance < -1)
        {
            var right = nodes[node.Right];
            if (Height(right.Right) < Height(right.Left))
            {
                Update(index, nodes[index] with
                {
                    Right = RotateRight(node.Right)
                });
            }
            return RotateLeft(index);
        }
        return index;
    }

    private int Height(int index) => index < 0 ? 0 : nodes[index].Height;

    private void Update(int index, DynamicTriangleNode node)
    {
        var minimum = node.Triangle.Minimum;
        var maximum = node.Triangle.Maximum;
        foreach (var child in (ReadOnlySpan<int>)[node.Left, node.Right])
        {
            if (child < 0)
            {
                continue;
            }
            var value = nodes[child];
            minimum = new V3(Math.Min(minimum.X, value.Minimum.X), Math.Min(minimum.Y, value.Minimum.Y), Math.Min(minimum.Z, value.Minimum.Z));
            maximum = new V3(Math.Max(maximum.X, value.Maximum.X), Math.Max(maximum.Y, value.Maximum.Y), Math.Max(maximum.Z, value.Maximum.Z));
        }
        nodes[index] = node with
        {
            Minimum = minimum,
            Maximum = maximum,
            Height = 1 + Math.Max(Height(node.Left), Height(node.Right))
        };
    }

    private int RotateLeft(int index)
    {
        var node = nodes[index];
        var next = nodes[node.Right];
        Update(index, node with
        {
            Right = next.Left
        });
        Update(node.Right, next with
        {
            Left = index
        });
        return node.Right;
    }

    private int RotateRight(int index)
    {
        var node = nodes[index];
        var next = nodes[node.Left];
        Update(index, node with
        {
            Left = next.Right
        });
        Update(node.Left, next with
        {
            Right = index
        });
        return node.Left;
    }

    public bool Intersects(SurfaceTriangle triangle) => Intersects(root, triangle, triangle.Minimum, triangle.Maximum);

    private bool Intersects(int index, SurfaceTriangle triangle, V3 minimum, V3 maximum)
    {
        if (index < 0)
        {
            return false;
        }
        var node = nodes[index];
        var x = minimum.X <= node.Maximum.X + 1e-8 && maximum.X >= node.Minimum.X - 1e-8;
        var y = minimum.Y <= node.Maximum.Y + 1e-8 && maximum.Y >= node.Minimum.Y - 1e-8;
        var z = minimum.Z <= node.Maximum.Z + 1e-8 && maximum.Z >= node.Minimum.Z - 1e-8;
        if (!(x && y && z))
        {
            return false;
        }
        return SurfaceTriangleIntersection.Intersects(triangle, node.Triangle)
            || Intersects(node.Left, triangle, minimum, maximum) || Intersects(node.Right, triangle, minimum, maximum);
    }
}
