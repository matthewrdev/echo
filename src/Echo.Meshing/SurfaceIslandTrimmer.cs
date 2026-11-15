using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Meshing;

public static class SurfaceIslandTrimmer
{
    public static SurfaceIslandTrimResult Trim(Surface source, IslandTrimMode mode, double minimumAreaPercent, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        if (!double.IsFinite(minimumAreaPercent) || minimumAreaPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumAreaPercent));
        }
        if (mode == IslandTrimMode.Off)
        {
            return new SurfaceIslandTrimResult { Surface = source };
        }
        if (source.SourceImages.Count != 0 && source.SourceImages.Count != source.Faces.Count)
        {
            throw new ArgumentException("Photograph assignments must be empty or match the face count.", nameof(source));
        }
        if (source.Faces.Count == 0)
        {
            return new SurfaceIslandTrimResult { Surface = source, InputComponents = 0 };
        }

        var parents = BuildComponents(source, cancellation);
        var areas = new double[source.Faces.Count];
        for (var index = 0; index < source.Faces.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            parents[index] = Root(parents, index);
            var face = source.Faces[index];
            var origin = new V3(source.Vertices[face[0]]);
            areas[parents[index]] += (new V3(source.Vertices[face[1]]) - origin)
                .Cross(new V3(source.Vertices[face[2]]) - origin).Length * .5;
        }

        var largest = parents[0];
        for (var index = 0; index < areas.Length; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (areas[index] > areas[largest])
            {
                largest = index;
            }
        }
        var threshold = areas[largest] * minimumAreaPercent / 100;
        var remove = new bool[areas.Length];
        var componentCount = 0;
        var removedCount = 0;
        var removedArea = 0d;
        for (var index = 0; index < areas.Length; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (parents[index] != index)
            {
                continue;
            }
            componentCount++;
            remove[index] = index != largest && (mode == IslandTrimMode.Main || areas[index] < threshold);
            if (remove[index])
            {
                removedCount++;
                removedArea += areas[index];
            }
        }
        var result = removedCount == 0 ? source : CopyRetainedFaces(source, parents, remove, cancellation);
        return new SurfaceIslandTrimResult
        {
            Surface = result,
            InputComponents = componentCount,
            RemovedComponents = removedCount,
            RemovedFaces = source.Faces.Count - result.Faces.Count,
            RemovedArea = removedArea
        };
    }

    private static int[] BuildComponents(Surface source, CancellationToken cancellation)
    {
        // Weld only exact positions for connectivity. Keep the original vertices
        // and photograph seams in the output; never bridge a physical gap.
        var positions = new Dictionary<V3, int>();
        var welded = new int[source.Vertices.Count];
        for (var index = 0; index < welded.Length; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var point = new V3(source.Vertices[index]);
            if (!positions.TryGetValue(point, out var id))
            {
                id = positions.Count;
                positions.Add(point, id);
            }
            welded[index] = id;
        }

        var parents = Enumerable.Range(0, source.Faces.Count).ToArray();
        var sizes = Enumerable.Repeat(1, parents.Length).ToArray();
        var edges = new Dictionary<SurfaceEdge, int>();
        for (var index = 0; index < source.Faces.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var face = source.Faces[index];
            for (var corner = 0; corner < 3; corner++)
            {
                var a = welded[face[corner]];
                var b = welded[face[(corner + 1) % 3]];
                if (a == b)
                {
                    continue;
                }
                var edge = new SurfaceEdge(Math.Min(a, b), Math.Max(a, b));
                if (edges.TryGetValue(edge, out var neighbour))
                {
                    var first = Root(parents, index);
                    var second = Root(parents, neighbour);
                    if (first == second)
                    {
                        continue;
                    }
                    if (sizes[first] < sizes[second])
                    {
                        parents[first] = second;
                        sizes[second] += sizes[first];
                    }
                    else
                    {
                        parents[second] = first;
                        sizes[first] += sizes[second];
                    }
                }
                else
                {
                    edges.Add(edge, index);
                }
            }
        }
        return parents;
    }

    private static Surface CopyRetainedFaces(Surface source, int[] parents, bool[] remove, CancellationToken cancellation)
    {
        var result = new Surface();
        var remap = new Dictionary<int, int>();
        for (var index = 0; index < source.Faces.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (remove[parents[index]])
            {
                continue;
            }
            var face = source.Faces[index];
            var output = new int[3];
            for (var corner = 0; corner < 3; corner++)
            {
                var vertex = face[corner];
                if (!remap.TryGetValue(vertex, out var mapped))
                {
                    mapped = result.Vertices.Count;
                    remap.Add(vertex, mapped);
                    result.Vertices.Add(source.Vertices[vertex]);
                }
                output[corner] = mapped;
            }
            result.Faces.Add(output);
            if (source.SourceImages.Count > 0)
            {
                result.SourceImages.Add(source.SourceImages[index]);
            }
        }
        return result;
    }

    private static int Root(int[] parents, int face)
    {
        while (parents[face] != face)
        {
            parents[face] = parents[parents[face]];
            face = parents[face];
        }
        return face;
    }
}
