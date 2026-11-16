using Echo.Contracts;
using Echo.Storage;
using Echo.Geometry;

namespace Echo.Meshing;

internal static class SurfaceBoundaryLoops
{
    public static IEnumerable<SurfaceBoundaryLoop> Find(Surface mesh, CancellationToken cancellation, WorkBuffers? work = null, Action<OperationProgress>? progress = null)
    {
        var faceCount = mesh.Faces.Count;
        IList<int> twins = work?.Filled(checked(faceCount * 3), -1, new IntRecordCodec()) ?? Enumerable.Repeat(-1, checked(faceCount * 3)).ToArray();
        var edges = new BinaryHashIndex<SurfaceEdge, int>(faceCount, new SurfaceEdgeRecordCodec(), new IntRecordCodec(), work, cancellation);
        IList<V3> normals = work?.Filled(faceCount, default(V3), new V3RecordCodec()) ?? new V3[faceCount];
        for (var index = 0; index < mesh.Faces.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (index % 10000 == 0)
            {
                progress?.Invoke(new OperationProgress("Boundary detection", "Finding open surface boundaries…", index, faceCount, "triangles"));
            }
            var face = mesh.Faces[index];
            var a = new V3(mesh.Vertices[face[0]]);
            normals[index] = (new V3(mesh.Vertices[face[1]]) - a).Cross(new V3(mesh.Vertices[face[2]]) - a).Unit;
            for (var corner = 0; corner < 3; corner++)
            {
                var from = face[corner];
                var to = face[(corner + 1) % 3];
                var key = new SurfaceEdge(Math.Min(from, to), Math.Max(from, to));
                var slot = index * 3 + corner;
                if (!edges.TryGetValue(key, out var other))
                {
                    edges.Add(key, slot);
                }
                else if (other >= 0 && twins[other] == -1)
                {
                    var opposite = mesh.Faces[other / 3][other % 3] == to;
                    twins[slot] = opposite ? other : -2;
                    twins[other] = opposite ? slot : -2;
                }
                else
                {
                    if (other >= 0)
                    {
                        if (twins[other] >= 0)
                        {
                            twins[twins[other]] = -2;
                        }
                        twins[other] = -2;
                    }
                    twins[slot] = -2;
                    edges[key] = -2;
                }
            }
        }

        IList<bool> visited = work?.Filled(twins.Count, false, new BoolRecordCodec()) ?? new bool[twins.Count];
        for (var start = 0; start < twins.Count; start++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (twins[start] != -1 || visited[start])
            {
                continue;
            }
            var boundary = new List<int>();
            var current = start;
            do
            {
                if (visited[current])
                {
                    break;
                }
                visited[current] = true;
                boundary.Add(current);
                current = Next(current);
                var steps = 0;
                while (twins[current] >= 0 && steps++ < twins.Count)
                {
                    current = Next(twins[current]);
                }
                if (twins[current] != -1 || steps >= twins.Count)
                {
                    break;
                }
            }
            while (current != start);
            if (current != start)
            {
                continue;
            }

            // Follow face fans at a pinched vertex instead of abandoning the
            // entire boundary. Split repeated vertices into simple cycles.
            var positions = new Dictionary<int, int>();
            var path = new List<int>();
            foreach (var halfedge in boundary.Append(start))
            {
                var vertex = mesh.Faces[halfedge / 3][halfedge % 3];
                if (positions.TryGetValue(vertex, out var first))
                {
                    var cycle = path.GetRange(first, path.Count - first);
                    if (cycle.Count >= 3)
                    {
                        var normal = new V3();
                        foreach (var edge in cycle)
                        {
                            normal += normals[edge / 3];
                        }
                        yield return new SurfaceBoundaryLoop
                        {
                            Vertices = cycle.Select(edge => mesh.Faces[edge / 3][edge % 3]).ToList(),
                            AdjacentNormal = normal.Unit
                        };
                    }
                    for (var index = first; index < path.Count; index++)
                    {
                        var edge = path[index];
                        positions.Remove(mesh.Faces[edge / 3][edge % 3]);
                    }
                    path.RemoveRange(first, path.Count - first);
                }
                positions[vertex] = path.Count;
                path.Add(halfedge);
            }
        }

    }

    private static int Next(int halfedge) => halfedge / 3 * 3 + (halfedge + 1) % 3;
}
