using Echo.Contracts;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Simplification;

internal static class ParallelMeshCollapse
{
    public static Surface Reduce(EdgeCollapse simplifier, Surface source, int target, CancellationToken cancellation,
        double detailTolerance, IReadOnlyList<double>? tolerances, WorkBuffers? work, Action<OperationProgress>? progress,
        int regionCount, int workers)
    {
        cancellation.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfLessThan(regionCount, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(workers, 1);
        if (!double.IsFinite(detailTolerance) || detailTolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(detailTolerance));
        }
        if (tolerances != null && (tolerances.Count != source.Vertices.Count || tolerances.Any(value => !double.IsFinite(value) || value < 0)))
        {
            throw new ArgumentException("Expected a finite nonnegative tolerance per source vertex.", nameof(tolerances));
        }
        var targetAlreadyMet = source.Faces.Count <= target && detailTolerance == 0 && tolerances == null;
        if (targetAlreadyMet || source.Vertices.Count == 0)
        {
            return source;
        }

        using var scratch = work?.CreateScope(preferResident: true);
        progress?.Invoke(new OperationProgress("Partition mesh", "Preparing independent regions with fixed shared boundaries…", 0, source.Faces.Count, "triangles"));
        IList<V3> positions = scratch?.List(new PositionRecordCodec()) ?? new List<V3>(source.Vertices.Count);
        var minimum = new double[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
        var maximum = new double[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
        foreach (var position in source.Vertices)
        {
            cancellation.ThrowIfCancellationRequested();
            positions.Add(new V3(position));
            for (var axis = 0; axis < 3; axis++)
            {
                minimum[axis] = Math.Min(minimum[axis], position[axis]);
                maximum[axis] = Math.Max(maximum[axis], position[axis]);
            }
        }
        var longest = 0;
        for (var axis = 1; axis < 3; axis++)
        {
            if (maximum[axis] - minimum[axis] > maximum[longest] - minimum[longest])
            {
                longest = axis;
            }
        }
        var extent = maximum[longest] - minimum[longest];
        if (!double.IsFinite(extent) || extent <= 0)
        {
            return simplifier.ReduceCore(source, target, cancellation, detailTolerance, tolerances, work, progress);
        }

        var regions = Enumerable.Range(0, regionCount)
            .Select(_ => new CollapseRegion(scratch?.CreateScope(), tolerances != null)).ToArray();
        IList<int> owners = scratch?.Filled(source.Vertices.Count, -1, new IntRecordCodec(), cancellation)
            ?? Enumerable.Repeat(-1, source.Vertices.Count).ToArray();
        var indexed = 0;
        foreach (var face in source.Faces)
        {
            cancellation.ThrowIfCancellationRequested();
            var centre = (Coordinate(positions[face[0]], longest) + Coordinate(positions[face[1]], longest) + Coordinate(positions[face[2]], longest)) / 3;
            var region = Math.Clamp((int)((centre - minimum[longest]) / extent * regionCount), 0, regionCount - 1);
            regions[region].Faces.Add(new FaceVertices(face[0], face[1], face[2]));
            foreach (var vertex in face)
            {
                var owner = owners[vertex];
                owners[vertex] = owner == -1 ? region : owner == region ? owner : -2;
            }
            if (++indexed % 65536 == 0)
            {
                progress?.Invoke(new OperationProgress("Partition mesh", $"Assigned {indexed:N0} triangles to independent regions.", indexed, source.Faces.Count, "triangles"));
            }
        }

        IList<int> localIndices = scratch?.Filled(source.Vertices.Count, -1, new IntRecordCodec(), cancellation)
            ?? Enumerable.Repeat(-1, source.Vertices.Count).ToArray();
        foreach (var region in regions)
        {
            for (var index = 0; index < region.Faces.Count; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                var face = region.Faces[index];
                region.Faces[index] = new FaceVertices(LocalVertex(face.A), LocalVertex(face.B), LocalVertex(face.C));
            }
            foreach (var original in region.OriginalVertices)
            {
                localIndices[original] = -1;
            }

            int LocalVertex(int original)
            {
                var local = localIndices[original];
                if (local < 0)
                {
                    local = region.Vertices.Count;
                    localIndices[original] = local;
                    region.Vertices.Add(positions[original]);
                    region.OriginalVertices.Add(original);
                    region.Locked.Add(owners[original] == -2);
                    region.Tolerances?.Add(tolerances![original]);
                }
                return local;
            }
        }
        (positions as IDisposable)?.Dispose();
        (owners as IDisposable)?.Dispose();
        (localIndices as IDisposable)?.Dispose();

        workers = Math.Min(workers, regionCount);
        var updates = new ParallelCollapseProgress(regionCount, workers, progress);
        progress?.Invoke(new OperationProgress("Parallel edge collapse", $"Simplifying {regionCount} independent regions with {workers} CPU workers…", 0, null, "candidates"));
        Parallel.For(0, regionCount, new ParallelOptions { CancellationToken = cancellation, MaxDegreeOfParallelism = workers }, index =>
        {
            var region = regions[index];
            // Error-limited local passes must not spend an equal face quota on
            // flat and detailed regions. The global pass assigns the final
            // ceiling after all regions have removed their redundant geometry.
            var hasDetailLimit = detailTolerance > 0 || tolerances != null;
            var regionalTarget = hasDetailLimit ? source.Faces.Count : Math.Max(target, source.Faces.Count / 2);
            var faceTarget = Math.Max(4, (int)Math.Ceiling(regionalTarget * (double)region.Faces.Count / source.Faces.Count));
            region.Reduced = simplifier.ReduceCore(region.Surface, faceTarget, cancellation, detailTolerance,
                (IReadOnlyList<double>?)region.Tolerances, region.Work, update => updates.Report(index, update),
                (IReadOnlyList<bool>)region.Locked, region.OutputVertices, region.OutputTolerances, outputStates: region.OutputStates);
            updates.Complete();
        });

        progress?.Invoke(new OperationProgress("Join mesh regions", "Joining fixed shared boundaries by original vertex identity…", 0, regionCount, "regions"));
        var joined = scratch?.Surface() ?? new Surface();
        IList<Vertex> joinedStates = scratch?.List(new VertexRecordCodec()) ?? new List<Vertex>();
        IList<int> remap = scratch?.Filled(source.Vertices.Count, -1, new IntRecordCodec(), cancellation)
            ?? Enumerable.Repeat(-1, source.Vertices.Count).ToArray();
        IList<double>? joinedTolerances = tolerances == null ? null : scratch?.List(new DoubleRecordCodec()) ?? new List<double>();
        foreach (var region in regions)
        {
            var reduced = region.Reduced!;
            for (var index = 0; index < reduced.Vertices.Count; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                var original = region.OriginalVertices[region.OutputVertices[index]];
                var mapped = remap[original];
                if (mapped < 0)
                {
                    mapped = joined.Vertices.Count;
                    remap[original] = mapped;
                    joined.Vertices.Add(reduced.Vertices[index]);
                    joinedStates.Add(region.OutputStates[index]);
                    joinedTolerances?.Add(region.OutputTolerances![index]);
                }
                else
                {
                    // A protected seam vertex occurs in several regions. Each
                    // contributes different source faces to its error metric.
                    var state = joinedStates[mapped];
                    var contribution = region.OutputStates[index];
                    state.PlaneCount += contribution.PlaneCount;
                    for (var coefficient = 0; coefficient < 16; coefficient++)
                    {
                        state[coefficient] += contribution[coefficient];
                    }
                    joinedStates[mapped] = state;
                }
                region.OutputVertices[index] = mapped;
            }
            foreach (var face in reduced.Faces)
            {
                cancellation.ThrowIfCancellationRequested();
                joined.Faces.Add([region.OutputVertices[face[0]], region.OutputVertices[face[1]], region.OutputVertices[face[2]]]);
            }
            region.Work?.Dispose();
        }
        (remap as IDisposable)?.Dispose();
        // The returned mesh must belong to the caller, not this private scope.
        // Request an output map to force compaction even if the ceiling is met.
        IList<int> outputVertices = scratch?.List(new IntRecordCodec()) ?? new List<int>();
        return simplifier.ReduceCore(joined, target, cancellation, detailTolerance, (IReadOnlyList<double>?)joinedTolerances,
            work, progress, outputVertices: outputVertices, initialVertices: (IReadOnlyList<Vertex>)joinedStates);
    }

    private static double Coordinate(V3 position, int axis) => axis == 0 ? position.X : axis == 1 ? position.Y : position.Z;
}
