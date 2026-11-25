using Echo.Geometry;
using Echo.Meshing.Geometry;
using Echo.Models;
using Echo.Storage;
using Echo.Storage.Records;
using Echo.Storage.Resources;

namespace Echo.Meshing.Repair;

public sealed class SurfaceDetailRefitter : ISurfaceDetailRefitter
{
    public Surface Refit(Surface surface, Surface observed, double maximumDistance, CancellationToken cancellation, WorkBuffers? work = null)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!double.IsFinite(maximumDistance) || maximumDistance <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDistance));
        }

        if (surface.Faces.Count == 0 || observed.Faces.Count == 0)
        {
            return surface;
        }

        using var scratch = work?.CreateScope();
        var topology = SurfaceTopologyBuilder.Build(surface, cancellation, scratch);
        var welded = topology.WeldedVertices;
        var points = scratch?.List(new V3RecordCodec()) ?? new List<V3>(surface.Vertices.Count);
        for (var index = 0; index < welded.Count; index++)
        {
            if (welded[index] == points.Count)
            {
                points.Add(new V3(surface.Vertices[index]));
            }
        }

        var heads = scratch?.Filled(points.Count, -1, new IntRecordCodec(), cancellation)
            ?? Enumerable.Repeat(-1, points.Count).ToArray();
        var links = scratch?.Filled(topology.Neighbours.Count, 0, new IntRecordCodec(), cancellation)
            ?? new int[topology.Neighbours.Count];
        var boundaries = scratch?.Filled(points.Count, false, new BoolRecordCodec(), cancellation) ?? new bool[points.Count];
        var faceNormals = scratch?.Filled(surface.Faces.Count, default(V3), new V3RecordCodec(), cancellation)
            ?? new V3[surface.Faces.Count];
        var vertexNormals = scratch?.Filled(points.Count, default(V3), new V3RecordCodec(), cancellation) ?? new V3[points.Count];
        for (var index = 0; index < surface.Faces.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var face = surface.Faces[index];
            var a = points[welded[face[0]]];
            var b = points[welded[face[1]]];
            var c = points[welded[face[2]]];
            var normal = (b - a).Cross(c - a);
            faceNormals[index] = normal;
            for (var corner = 0; corner < 3; corner++)
            {
                var slot = index * 3 + corner;
                var vertex = welded[face[corner]];
                links[slot] = heads[vertex];
                heads[vertex] = slot;
                vertexNormals[vertex] += normal;
                if (topology.Neighbours[slot] < 0)
                {
                    boundaries[vertex] = true;
                    boundaries[welded[face[(corner + 1) % 3]]] = true;
                }
            }
        }

        using var reference = new SurfaceTriangleIndex(observed, cancellation, scratch);
        var fitted = scratch?.List(new V3RecordCodec()) ?? new List<V3>(points.Count);
        foreach (var point in points)
        {
            fitted.Add(point);
        }

        for (var vertex = 0; vertex < points.Count; vertex++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (heads[vertex] < 0 || boundaries[vertex])
            {
                continue;
            }

            var position = points[vertex];
            var normal = vertexNormals[vertex].Unit;
            var nearest = reference.Nearest(position, maximumDistance);
            if (nearest.Normal.Dot(normal) < .65)
            {
                continue;
            }

            var offset = (nearest.Position - position).Dot(normal);
            var step = normal * offset;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var candidate = position + step;
                if (PreservesFaces(surface, welded, heads[vertex], links, faceNormals, fitted, vertex, candidate))
                {
                    fitted[vertex] = candidate;
                    break;
                }

                step *= .5;
            }
        }

        var output = work?.Surface() ?? new Surface();
        foreach (var vertex in welded)
        {
            output.Vertices.Add(fitted[vertex].Array);
        }

        foreach (var face in surface.Faces)
        {
            output.Faces.Add(face);
        }

        foreach (var image in surface.SourceImages)
        {
            output.SourceImages.Add(image);
        }

        return output;
    }

    private bool PreservesFaces(Surface surface, IList<int> welded, int first, IList<int> links,
        IList<V3> faceNormals, IList<V3> fitted, int vertex, V3 candidate)
    {
        for (var slot = first; slot >= 0; slot = links[slot])
        {
            var index = slot / 3;
            var face = surface.Faces[index];
            var a = Position(welded[face[0]], vertex, fitted, candidate);
            var b = Position(welded[face[1]], vertex, fitted, candidate);
            var c = Position(welded[face[2]], vertex, fitted, candidate);
            var normal = (b - a).Cross(c - a);
            var original = faceNormals[index];
            var length = normal.Length;
            if (length < original.Length * .6 || normal.Dot(original) < length * original.Length * .85)
            {
                return false;
            }
        }

        return true;
    }

    private V3 Position(int vertex, int moved, IList<V3> fitted, V3 candidate) => vertex == moved ? candidate : fitted[vertex];
}
