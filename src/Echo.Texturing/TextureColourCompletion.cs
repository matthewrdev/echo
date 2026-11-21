using Echo.Models;
using Echo.Geometry;

namespace Echo.Texturing;

internal static class TextureColourCompletion
{
    public static V3[] Extend(Surface mesh, IList<int> welded, V3[] sums, int[] weights, CancellationToken cancellation)
    {
        var colours = new V3[sums.Length];
        var neighbours = new List<int>?[sums.Length];
        var known = new bool[sums.Length];
        var mean = new V3();
        var count = 0;
        for (var vertex = 0; vertex < colours.Length; vertex++)
        {
            if (weights[vertex] > 0)
            {
                colours[vertex] = sums[vertex] / weights[vertex];
                mean += colours[vertex];
                count++;
                known[vertex] = true;
            }
        }
        mean = count > 0 ? mean / count : new V3(128, 128, 128);
        for (var index = 0; index < mesh.Faces.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(mesh.SourceImages[index]))
            {
                continue;
            }
            var face = mesh.Faces[index];
            for (var corner = 0; corner < 3; corner++)
            {
                var a = welded[face[corner]];
                var b = welded[face[(corner + 1) % 3]];
                neighbours[a] ??= [];
                neighbours[b] ??= [];
                if (!neighbours[a]!.Contains(b))
                {
                    neighbours[a]!.Add(b);
                    neighbours[b]!.Add(a);
                }
            }
        }

        // Seed missing vertices from the closest observed boundary along the
        // surface, then solve a discrete harmonic colour field. Shared welded
        // vertices receive identical colours even across separate UV charts.
        var reached = (bool[])known.Clone();
        var pending = new Queue<int>();
        for (var vertex = 0; vertex < neighbours.Length; vertex++)
        {
            if (known[vertex] && neighbours[vertex] != null)
            {
                pending.Enqueue(vertex);
            }
        }
        while (pending.TryDequeue(out var vertex))
        {
            cancellation.ThrowIfCancellationRequested();
            foreach (var other in neighbours[vertex]!)
            {
                if (!reached[other])
                {
                    reached[other] = true;
                    colours[other] = colours[vertex];
                    pending.Enqueue(other);
                }
            }
        }
        var unknown = Enumerable.Range(0, neighbours.Length).Where(vertex => neighbours[vertex] != null && !known[vertex]).ToArray();
        foreach (var vertex in unknown)
        {
            if (!reached[vertex])
            {
                colours[vertex] = mean;
            }
        }
        var next = new V3[unknown.Length];
        for (var pass = 0; pass < 200; pass++)
        {
            cancellation.ThrowIfCancellationRequested();
            var maximumChange = 0.0;
            for (var index = 0; index < unknown.Length; index++)
            {
                var vertex = unknown[index];
                var adjacent = neighbours[vertex]!;
                var sum = new V3();
                foreach (var other in adjacent)
                {
                    sum += colours[other];
                }
                next[index] = (colours[vertex] + sum / adjacent.Count) * .5;
                maximumChange = Math.Max(maximumChange, (next[index] - colours[vertex]).Length);
            }
            for (var index = 0; index < unknown.Length; index++)
            {
                colours[unknown[index]] = next[index];
            }
            if (maximumChange < .01)
            {
                break;
            }
        }
        return colours;
    }
}
