using Echo.Geometry;
using Echo.Meshing.Geometry;
using Echo.Meshing.Topology;
using Echo.Models;
using Echo.Storage.Pages;
using Echo.Storage.Records;
using Echo.Storage.Resources;
using Echo.Storage;

namespace Echo.Meshing.Repair;

internal static class SurfaceBoundaryLoops
{
    public static IEnumerable<SurfaceBoundaryLoop> Find(Surface mesh, CancellationToken cancellation, WorkBuffers? work = null, Action<OperationProgress>? progress = null,
        BinaryHashIndex<SurfaceEdge, BoundaryEdge>? repairEdges = null, SurfaceTopologyReport? topology = null)
    {
        using var scratch = work?.CreateScope();
        work = scratch;
        var faceCount = mesh.Faces.Count;
        var reader = new SurfaceGeometryReader(mesh);
        IList<int> twins = work?.Filled(checked(faceCount * 3), -1, new IntRecordCodec()) ?? Enumerable.Repeat(-1, checked(faceCount * 3)).ToArray();
        var edges = repairEdges == null ? new BinaryHashIndex<SurfaceEdge, int>(faceCount, new SurfaceEdgeRecordCodec(), new IntRecordCodec(), work, cancellation) : null;
        IList<V3> normals = work?.Filled(faceCount, default(V3), new V3RecordCodec()) ?? new V3[faceCount];
        var faceBatch = new SurfaceTriangleVertices[Math.Min(1024, faceCount)];
        for (var first = 0; first < faceCount; first += faceBatch.Length)
        {
            var count = Math.Min(faceBatch.Length, faceCount - first);
            reader.ReadFaces(first, faceBatch.AsSpan(0, count));
            for (var relative = 0; relative < count; relative++)
            {
                var index = first + relative;
                cancellation.ThrowIfCancellationRequested();
                if (index % 10000 == 0)
                {
                    progress?.Invoke(new OperationProgress("Boundary detection", "Finding open surface boundaries…", index, faceCount, "triangles"));
                }

                var face = faceBatch[relative];
                var a = reader.Vertex(face.A);
                var normal = (reader.Vertex(face.B) - a).Cross(reader.Vertex(face.C) - a);
                normals[index] = normal.Unit;
                if (topology != null)
                {
                    var area = normal.Length;
                    if (!double.IsFinite(area) || area <= 0)
                    {
                        topology.DegenerateFaces++;
                    }
                }

                for (var corner = 0; corner < 3; corner++)
                {
                    var from = face[corner];
                    var to = face[(corner + 1) % 3];
                    var key = new SurfaceEdge(Math.Min(from, to), Math.Max(from, to));
                    var slot = index * 3 + corner;
                    var other = -1;
                    bool found;
                    var opposite = false;
                    if (repairEdges != null)
                    {
                        found = repairEdges.TryGetValue(key, out var existing);
                        other = existing.Halfedge;
                        opposite = existing.From == to;
                        if (topology != null && found && existing.Count == 1 && existing.From == from)
                        {
                            topology.WindingConflicts++;
                        }

                        repairEdges[key] = found
                            ? existing with
                            {
                                Count = existing.Count + 1
                            }
                            : new BoundaryEdge(from, to, normals[index], 1, slot);
                    }
                    else
                    {
                        found = edges!.TryGetValue(key, out other);
                        opposite = found && other >= 0 && reader.Face(other / 3)[other % 3] == to;
                        if (!found)
                        {
                            edges.Add(key, slot);
                        }
                    }

                    if (!found)
                    {
                        continue;
                    }
                    else if (other >= 0 && twins[other] == -1)
                    {
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
                        if (repairEdges != null)
                        {
                            repairEdges[key] = repairEdges[key] with
                            {
                                Halfedge = -2
                            };
                        }
                        else
                        {
                            edges![key] = -2;
                        }
                    }
                }
            }
        }

        if (topology != null)
        {
            foreach (var edge in repairEdges!.Values)
            {
                cancellation.ThrowIfCancellationRequested();
                if (edge.Count == 1)
                {
                    topology.BoundaryEdges++;
                }
                else if (edge.Count > 2)
                {
                    topology.NonManifoldEdges++;
                }
            }

            if (topology.BoundaryEdges == 0)
            {
                yield break;
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
                var vertex = reader.Face(halfedge / 3)[halfedge % 3];
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
                            Vertices = cycle.Select(edge => reader.Face(edge / 3)[edge % 3]).ToList(),
                            AdjacentNormal = normal.Unit
                        };
                    }

                    for (var index = first; index < path.Count; index++)
                    {
                        var edge = path[index];
                        positions.Remove(reader.Face(edge / 3)[edge % 3]);
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
