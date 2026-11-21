using Echo.Models;
using Echo.Storage.Pages;
using Echo.Storage.Records;
using Echo.Storage.Resources;
using Echo.Storage;

namespace Echo.Geometry;

public static class SurfaceTopologyBuilder
{
    public static SurfaceTopology Build(Surface mesh, CancellationToken cancellation, WorkBuffers? work = null)
    {
        using var scratch = work?.CreateScope();
        var positions = new BinaryHashIndex<V3, int>(mesh.Vertices.Count, new PositionKeyCodec(), new IntRecordCodec(), scratch, cancellation);
        IList<int> welded = work?.Filled(mesh.Vertices.Count, 0, new IntRecordCodec()) ?? new int[mesh.Vertices.Count];
        for (var index = 0; index < welded.Count; index++)
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

        IList<int> neighbours = work?.Filled(checked(mesh.Faces.Count * 3), -1, new IntRecordCodec()) ?? Enumerable.Repeat(-1, checked(mesh.Faces.Count * 3)).ToArray();
        // Hash both endpoints independently. UInt64's XOR hash collapses packed
        // consecutive vertex IDs into a handful of buckets on regular meshes.
        var edges = new BinaryHashIndex<SurfaceTopologyEdge, int>(mesh.Faces.Count, new SurfaceTopologyEdgeCodec(), new IntRecordCodec(), scratch, cancellation);
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
