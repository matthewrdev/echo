using Echo.Models;
using Echo.Geometry;

namespace Echo.Texturing;

internal sealed class TexturePriorityField
{
    private readonly TextureBrushEntry[] entries;
    private readonly double[] weights;
    private readonly TextureBrushNode? root;

    public TexturePriorityField(TexturePriorityPainting painting, CancellationToken cancellation)
    {
        painting.Validate();
        weights = painting.Strokes.Select(stroke => Math.Log2(stroke.Weight)).ToArray();
        entries = painting.Strokes.SelectMany((stroke, index) => stroke.Samples.Select(sample =>
            new TextureBrushEntry(new V3(sample.Centre), new V3(sample.Normal), sample.Radius, sample.Depth, index))).ToArray();
        root = entries.Length == 0 ? null : Build(0, entries.Length, cancellation);
    }

    public double LogWeight(V3 point, SortedDictionary<int, double> influences)
    {
        influences.Clear();
        Query(root, point, influences);
        var weight = 0d;
        foreach (var influence in influences)
        {
            // A stroke has one strength even where its samples overlap. Later
            // strokes replace earlier priorities, including a neutral eraser.
            weight += (weights[influence.Key] - weight) * influence.Value;
        }
        return weight;
    }

    private TextureBrushNode Build(int start, int count, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var minimum = new V3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        var maximum = new V3(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
        for (var index = start; index < start + count; index++)
        {
            var entry = entries[index];
            var p = entry.Centre;
            var r = entry.Radius;
            minimum = new V3(Math.Min(minimum.X, p.X - r), Math.Min(minimum.Y, p.Y - r), Math.Min(minimum.Z, p.Z - r));
            maximum = new V3(Math.Max(maximum.X, p.X + r), Math.Max(maximum.Y, p.Y + r), Math.Max(maximum.Z, p.Z + r));
        }
        var node = new TextureBrushNode { Minimum = minimum, Maximum = maximum, Start = start, Count = count };
        if (count > 8)
        {
            var span = maximum - minimum;
            var axis = span.X >= span.Y && span.X >= span.Z ? 0 : span.Y >= span.Z ? 1 : 2;
            Array.Sort(entries, start, count, Comparer<TextureBrushEntry>.Create((a, b) =>
                Coordinate(a.Centre, axis).CompareTo(Coordinate(b.Centre, axis))));
            node.Left = Build(start, count / 2, cancellation);
            node.Right = Build(start + count / 2, count - count / 2, cancellation);
        }
        return node;
    }

    private void Query(TextureBrushNode? node, V3 point, SortedDictionary<int, double> influences)
    {
        if (node == null)
        {
            return;
        }
        var outsideX = point.X < node.Minimum.X || point.X > node.Maximum.X;
        var outsideY = point.Y < node.Minimum.Y || point.Y > node.Maximum.Y;
        var outsideZ = point.Z < node.Minimum.Z || point.Z > node.Maximum.Z;
        if (outsideX || outsideY || outsideZ)
        {
            return;
        }
        if (node.Left != null)
        {
            Query(node.Left, point, influences);
            Query(node.Right, point, influences);
            return;
        }
        for (var index = node.Start; index < node.Start + node.Count; index++)
        {
            var entry = entries[index];
            var delta = point - entry.Centre;
            var along = delta.Dot(entry.Normal);
            var radial = delta - entry.Normal * along;
            var distance = Math.Sqrt(radial.Dot(radial) / (entry.Radius * entry.Radius) + along * along / (entry.Depth * entry.Depth));
            if (distance >= 1)
            {
                continue;
            }
            var feather = Math.Clamp((1 - distance) * 2, 0, 1);
            var influence = feather * feather * (3 - 2 * feather);
            influences[entry.Stroke] = Math.Max(influences.GetValueOrDefault(entry.Stroke), influence);
        }
    }

    private double Coordinate(V3 point, int axis) => axis == 0 ? point.X : axis == 1 ? point.Y : point.Z;
}
