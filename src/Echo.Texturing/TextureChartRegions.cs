using Echo.Geometry;

namespace Echo.Texturing;

internal static class TextureChartRegions
{
    public static List<List<int>> Build(TextureChartGeometry geometry, int maximumFaces, double angle,
        IReadOnlyList<bool>? materials, CancellationToken cancellation)
    {
        var owner = new int[geometry.Surface.Faces.Count];
        Array.Fill(owner, -1);
        var regions = new List<List<int>>();
        var seeds = Enumerable.Range(0, owner.Length).OrderByDescending(index => geometry.Areas[index]);
        foreach (var seed in seeds)
        {
            cancellation.ThrowIfCancellationRequested();
            if (owner[seed] >= 0)
            {
                continue;
            }
            var region = Grow(geometry, owner, regions.Count, seed, maximumFaces, angle, materials, cancellation);
            regions.Add(region);
        }

        MergeSmall(geometry, regions, owner, maximumFaces, materials, cancellation);
        return regions.Where(region => region.Count > 0).ToList();
    }

    private static List<int> Grow(TextureChartGeometry geometry, int[] owner, int id, int seed,
        int maximumFaces, double angle, IReadOnlyList<bool>? materials, CancellationToken cancellation)
    {
        var region = new List<int>();
        var vertices = new HashSet<int>();
        var pending = new PriorityQueue<int, double>();
        pending.Enqueue(seed, 0);
        var normalPenalty = 1 / Math.Max(.05, 1 - Math.Cos(angle * Math.PI / 180));
        while (pending.TryDequeue(out var face, out _) && region.Count < maximumFaces)
        {
            cancellation.ThrowIfCancellationRequested();
            var differentMaterial = materials != null && materials[face] != materials[seed];
            if (owner[face] >= 0 || differentMaterial)
            {
                continue;
            }
            if (region.Count > 0 && !CanAppend(geometry, face, vertices, owner, id))
            {
                continue;
            }
            owner[face] = id;
            region.Add(face);
            foreach (var vertex in geometry.Surface.Faces[face])
            {
                vertices.Add(geometry.WeldedVertices[vertex]);
            }
            for (var edge = 0; edge < 3; edge++)
            {
                var adjacent = geometry.Neighbours[face * 3 + edge];
                if (adjacent < 0 || owner[adjacent] >= 0)
                {
                    continue;
                }
                var delta = geometry.Centres[adjacent] - geometry.Centres[seed];
                var bend = 1 - geometry.Normals[seed].Dot(geometry.Normals[adjacent]);
                // Prefer compact regions and broad shape continuity. Local folds
                // are a cost, not a mandatory UV seam; the unwrap measures stretch.
                var cost = delta.Dot(delta) * (1 + bend * normalPenalty);
                pending.Enqueue(adjacent, cost);
            }
        }
        return region;
    }

    private static bool CanAppend(TextureChartGeometry geometry, int face, HashSet<int> vertices, int[] owner, int id)
    {
        var sharedEdges = 0;
        var sharedVertices = 0;
        for (var corner = 0; corner < 3; corner++)
        {
            var adjacent = geometry.Neighbours[face * 3 + corner];
            if (adjacent >= 0 && owner[adjacent] == id)
            {
                sharedEdges++;
            }
            if (vertices.Contains(geometry.WeldedVertices[geometry.Surface.Faces[face][corner]]))
            {
                sharedVertices++;
            }
        }
        // Attach along a connected boundary arc, retaining disk topology.
        // In particular, do not join two existing vertices through a pinched tip.
        var extendsBoundary = sharedEdges == 1 && sharedVertices == 2;
        return sharedEdges == 2 || extendsBoundary;
    }

    private static void MergeSmall(TextureChartGeometry geometry, List<List<int>> regions, int[] owner,
        int maximumFaces, IReadOnlyList<bool>? materials, CancellationToken cancellation)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            foreach (var id in Enumerable.Range(0, regions.Count).OrderBy(index => regions[index].Count))
            {
                cancellation.ThrowIfCancellationRequested();
                var region = regions[id];
                if (region.Count == 0 || region.Count >= Math.Max(8, maximumFaces / 4))
                {
                    continue;
                }
                var neighbours = new Dictionary<int, int>();
                foreach (var face in region)
                {
                    for (var edge = 0; edge < 3; edge++)
                    {
                        var adjacent = geometry.Neighbours[face * 3 + edge];
                        if (adjacent < 0 || owner[adjacent] == id)
                        {
                            continue;
                        }
                        var other = owner[adjacent];
                        var differentMaterial = materials != null && materials[adjacent] != materials[face];
                        if (!differentMaterial && region.Count + regions[other].Count <= maximumFaces)
                        {
                            neighbours[other] = neighbours.GetValueOrDefault(other) + 1;
                        }
                    }
                }
                foreach (var other in neighbours.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).Select(pair => pair.Key))
                {
                    var union = regions[other].Concat(region).ToList();
                    if (new TextureChartMesh(geometry, union).Boundary.Length == 0)
                    {
                        continue;
                    }
                    regions[other] = union;
                    foreach (var face in region)
                    {
                        owner[face] = other;
                    }
                    region.Clear();
                    break;
                }
            }
        }
    }

    public static List<List<int>> Split(TextureChartGeometry geometry, List<int> faces, CancellationToken cancellation)
    {
        var centre = default(V3);
        foreach (var face in faces)
        {
            centre += geometry.Centres[face];
        }
        centre /= faces.Count;
        var seed = faces.MaxBy(face => (geometry.Centres[face] - centre).Length);
        var remaining = faces.ToHashSet();
        var first = new List<int>();
        var pending = new Queue<int>();
        pending.Enqueue(seed);
        while (pending.TryDequeue(out var face) && first.Count < Math.Max(1, faces.Count / 2))
        {
            cancellation.ThrowIfCancellationRequested();
            if (!remaining.Remove(face))
            {
                continue;
            }
            first.Add(face);
            for (var edge = 0; edge < 3; edge++)
            {
                var adjacent = geometry.Neighbours[face * 3 + edge];
                if (remaining.Contains(adjacent))
                {
                    pending.Enqueue(adjacent);
                }
            }
        }
        var parts = new List<List<int>> { first };
        pending.Clear();
        foreach (var start in faces)
        {
            if (!remaining.Contains(start))
            {
                continue;
            }
            var part = new List<int>();
            pending.Enqueue(start);
            while (pending.TryDequeue(out var face))
            {
                cancellation.ThrowIfCancellationRequested();
                if (!remaining.Remove(face))
                {
                    continue;
                }
                part.Add(face);
                for (var edge = 0; edge < 3; edge++)
                {
                    var adjacent = geometry.Neighbours[face * 3 + edge];
                    if (remaining.Contains(adjacent))
                    {
                        pending.Enqueue(adjacent);
                    }
                }
            }
            parts.Add(part);
        }
        return parts;
    }
}
