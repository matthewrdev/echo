using Echo.Contracts;

namespace Echo.Geometry;

public static class SurfaceTopologyBuilder
{
    public static SurfaceTopology Build(Surface mesh, CancellationToken cancellation)
    {
        var positions = new Dictionary<V3, int>();
        var welded = new int[mesh.Vertices.Count];
        for (var index = 0; index < welded.Length; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var point = new V3(mesh.Vertices[index]);
            if (!positions.TryGetValue(point, out var id))
            {
                id = positions.Count;
                positions.Add(point, id);
            }
            welded[index] = id;
        }

        var neighbours = new int[mesh.Faces.Count * 3];
        Array.Fill(neighbours, -1);
        // Hash both endpoints independently. UInt64's XOR hash collapses packed
        // consecutive vertex IDs into a handful of buckets on regular meshes.
        var edges = new Dictionary<SurfaceTopologyEdge, int>();
        for (var index = 0; index < mesh.Faces.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var face = mesh.Faces[index];
            for (var corner = 0; corner < 3; corner++)
            {
                var a = welded[face[corner]];
                var b = welded[face[(corner + 1) % 3]];
                var key = new SurfaceTopologyEdge(Math.Min(a, b), Math.Max(a, b));
                var slot = index * 3 + corner;
                if (!edges.TryGetValue(key, out var other))
                {
                    edges.Add(key, slot);
                }
                else if (other >= 0)
                {
                    if (neighbours[other] < 0)
                    {
                        neighbours[slot] = other / 3;
                        neighbours[other] = index;
                    }
                    else
                    {
                        // An ambiguous edge must not connect unrelated sheets.
                        var second = neighbours[other];
                        for (var edge = 0; edge < 3; edge++)
                        {
                            if (neighbours[second * 3 + edge] == other / 3)
                            {
                                neighbours[second * 3 + edge] = -1;
                            }
                        }
                        neighbours[other] = -1;
                        edges[key] = -1;
                    }
                }
            }
        }
        return new SurfaceTopology { WeldedVertices = welded, Neighbours = neighbours };
    }
}
