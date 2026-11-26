using Echo.Models;

namespace Echo.Export.Terrain;

internal static class TerrainStitchRing
{
    public static int[] Build(TerrainWall wall, int columns, int vertexOffset)
    {
        var centerX = wall.Boundary.Average(index => wall.GridPositions[index][0]);
        var centerY = wall.Boundary.Average(index => wall.GridPositions[index][1]);
        TerrainRimVertex Vertex(int index, double x, double y) => new(index, x, y, Math.Atan2(y - centerY, x - centerX));
        var inside = wall.Boundary.Select(index => Vertex(index + vertexOffset, wall.GridPositions[index][0], wall.GridPositions[index][1])).ToArray();
        var outside = wall.Window.Boundary(columns).Select(index => Vertex(index, index % columns, index / columns)).ToArray();
        var first = Array.IndexOf(inside, inside.MinBy(point => point.Angle));
        inside = [.. inside.Skip(first), .. inside.Take(first)];
        for (var index = 0; index < inside.Length; index++)
        {
            var current = inside[index];
            var next = inside[(index + 1) % inside.Length];
            var centreInside = Area(current, next, Vertex(-1, centerX, centerY)) > 1e-9;
            if (!centreInside || (index + 1 < inside.Length && next.Angle <= current.Angle + 1e-9))
            {
                throw new EchoException($"{wall.Name} has a folded or non-radial footprint. Choose a simpler open crop.", 2);
            }
        }

        var angles = inside.Select(point => point.Angle).ToArray();
        foreach (var point in wall.GridPositions)
        {
            var sample = Vertex(-1, point[0], point[1]);
            var segment = Array.BinarySearch(angles, sample.Angle);
            if (segment < 0)
            {
                segment = (~segment + angles.Length - 1) % angles.Length;
            }

            if (Area(inside[segment], inside[(segment + 1) % inside.Length], sample) < -1e-7)
            {
                throw new EchoException($"{wall.Name} projects beyond its open rim. Choose a crop that encloses the wall geometry.", 2);
            }
        }

        outside = outside.OrderBy(point => point.Angle).ToArray();
        var faces = new List<int>((inside.Length + outside.Length) * 3);
        var currentInside = inside[^1];
        var currentOutside = outside[^1];
        var nextInside = 0;
        var nextOutside = 0;
        while (nextInside < inside.Length || nextOutside < outside.Length)
        {
            var advanceInside = nextInside < inside.Length
                && (nextOutside == outside.Length || inside[nextInside].Angle < outside[nextOutside].Angle);
            var next = advanceInside ? inside[nextInside++] : outside[nextOutside++];
            if (Area(currentOutside, currentInside, next) >= -1e-9)
            {
                throw new EchoException($"{wall.Name} could not be stitched without a folded terrain seam. Adjust the scan crop.", 2);
            }

            faces.Add(currentOutside.Index);
            faces.Add(currentInside.Index);
            faces.Add(next.Index);
            if (advanceInside)
            {
                currentInside = next;
            }
            else
            {
                currentOutside = next;
            }
        }

        return faces.ToArray();
    }

    private static double Area(TerrainRimVertex a, TerrainRimVertex b, TerrainRimVertex c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
}
