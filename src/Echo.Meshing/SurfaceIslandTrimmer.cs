using Echo.Contracts;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Meshing;

public static class SurfaceIslandTrimmer
{
    public static SurfaceIslandTrimResult Trim(Surface source, IslandTrimMode mode, double minimumAreaPercent, CancellationToken cancellation, WorkBuffers? work = null, Action<OperationProgress>? progress = null)
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

        var parents = BuildComponents(source, cancellation, work, progress);
        IList<double> areas = work?.Filled(source.Faces.Count, 0d, new DoubleRecordCodec()) ?? new double[source.Faces.Count];
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
        for (var index = 0; index < areas.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (areas[index] > areas[largest])
            {
                largest = index;
            }
        }
        var threshold = areas[largest] * minimumAreaPercent / 100;
        IList<bool> remove = work?.Filled(areas.Count, false, new BoolRecordCodec()) ?? new bool[areas.Count];
        var componentCount = 0;
        var removedCount = 0;
        var removedArea = 0d;
        for (var index = 0; index < areas.Count; index++)
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
        var result = removedCount == 0 ? source : CopyRetainedFaces(source, parents, remove, cancellation, work);
        return new SurfaceIslandTrimResult
        {
            Surface = result,
            InputComponents = componentCount,
            RemovedComponents = removedCount,
            RemovedFaces = source.Faces.Count - result.Faces.Count,
            RemovedArea = removedArea
        };
    }

    private static IList<int> BuildComponents(Surface source, CancellationToken cancellation, WorkBuffers? work, Action<OperationProgress>? progress)
    {
        // Weld only exact positions for connectivity. Keep the original vertices
        // and photograph seams in the output; never bridge a physical gap.
        var positions = work?.Map(new PositionKeyCodec(), new IntRecordCodec()) ?? new Dictionary<V3, int>();
        IList<int> welded = work?.Filled(source.Vertices.Count, 0, new IntRecordCodec()) ?? new int[source.Vertices.Count];
        for (var index = 0; index < welded.Count; index++)
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

        IList<int> parents = work?.List(new IntRecordCodec()) ?? new List<int>(source.Faces.Count);
        for (var index = 0; index < source.Faces.Count; index++)
        {
            parents.Add(index);
        }
        IList<int> sizes = work?.Filled(parents.Count, 1, new IntRecordCodec()) ?? Enumerable.Repeat(1, parents.Count).ToArray();
        var edges = work?.Map(new SurfaceEdgeRecordCodec(), new IntRecordCodec()) ?? new Dictionary<SurfaceEdge, int>();
        for (var index = 0; index < source.Faces.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (index % 10000 == 0)
            {
                progress?.Invoke(new OperationProgress("Island connectivity", "Connecting triangles across shared edges…", index, source.Faces.Count, "triangles"));
            }
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

    private static Surface CopyRetainedFaces(Surface source, IList<int> parents, IList<bool> remove, CancellationToken cancellation, WorkBuffers? work)
    {
        var result = work?.Surface() ?? new Surface();
        var remap = work?.Map(new IntRecordCodec(), new IntRecordCodec()) ?? new Dictionary<int, int>();
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

    private static int Root(IList<int> parents, int face)
    {
        while (parents[face] != face)
        {
            parents[face] = parents[parents[face]];
            face = parents[face];
        }
        return face;
    }
}
