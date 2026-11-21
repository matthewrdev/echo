using Echo.Geometry;
using Echo.Models;
using Echo.Storage.Records;
using Echo.Storage.Resources;
using Echo.Storage;

namespace Echo.Simplification;

public sealed class EdgeCollapse : IEdgeCollapse
{

    public EdgeCollapse()
    {
    }

    public Surface Reduce(Surface sourceMesh, int targetFaceCount, CancellationToken cancellation, double detailTolerance = 0, IReadOnlyList<double>? vertexTolerances = null, WorkBuffers? work = null, Action<OperationProgress>? progress = null)
    {
        var regions = Math.Min(16, sourceMesh.Faces.Count / 262144);
        if (regions >= 2)
        {
            return ParallelMeshCollapse.Reduce(this, sourceMesh, targetFaceCount, cancellation, detailTolerance,
                vertexTolerances, work, progress, regions, Math.Min(8, Environment.ProcessorCount));
        }
        return ReduceCore(sourceMesh, targetFaceCount, cancellation, detailTolerance, vertexTolerances, work, progress);
    }

    internal Surface ReduceCore(Surface sourceMesh, int targetFaceCount, CancellationToken cancellation, double detailTolerance = 0,
        IReadOnlyList<double>? vertexTolerances = null, WorkBuffers? work = null, Action<OperationProgress>? progress = null,
        IReadOnlyList<bool>? lockedVertices = null, IList<int>? outputVertices = null, IList<double>? outputTolerances = null,
        IReadOnlyList<Vertex>? initialVertices = null, IList<Vertex>? outputStates = null)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!double.IsFinite(detailTolerance) || detailTolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(detailTolerance));
        }
        if (vertexTolerances != null && (vertexTolerances.Count != sourceMesh.Vertices.Count
            || vertexTolerances.Any(value => !double.IsFinite(value) || value < 0)))
        {
            throw new ArgumentException("Expected a finite nonnegative tolerance per source vertex.", nameof(vertexTolerances));
        }
        var outputWork = work;
        using var scratch = work?.CreateScope(preferResident: true);
        work = scratch;
        IList<double>? tolerances = null;
        if (vertexTolerances != null)
        {
            tolerances = work?.List(new DoubleRecordCodec()) ?? new List<double>(vertexTolerances.Count);
            foreach (var tolerance in vertexTolerances)
            {
                tolerances.Add(tolerance);
            }
        }
        if (sourceMesh.Faces.Count <= targetFaceCount && detailTolerance == 0 && tolerances == null && outputVertices == null)
        {
            return sourceMesh;
        }

        var updates = new SimplificationProgress(progress);
        var adjacencyRecords = work == null ? 0 : sourceMesh.Vertices.Count;
        updates.Start(SimplificationPhase.Vertices, sourceMesh.Vertices.Count + (long)adjacencyRecords, "records");
        var adjacency = new VertexFacesStore(sourceMesh.Vertices.Count, work, cancellation, count => updates.Advance(work == null ? 0 : count));
        IList<Vertex> vertices = work?.List(new VertexRecordCodec()) ?? new List<Vertex>(sourceMesh.Vertices.Count);
        IList<int> revisions = work?.List(new IntRecordCodec()) ?? new List<int>(sourceMesh.Vertices.Count);
        IList<BoundaryState> boundaries = work?.List(new BoundaryStateRecordCodec()) ?? new List<BoundaryState>(sourceMesh.Vertices.Count);
        var vertexIndex = 0;
        foreach (var position in SimplificationGeometry.Vertices(sourceMesh))
        {
            cancellation.ThrowIfCancellationRequested();
            var index = vertexIndex++;
            var vertex = initialVertices == null
                ? new Vertex { Id = index, Position = position }
                : initialVertices[index] with
                {
                    Id = index,
                    Revision = 0,
                    Removed = false
                };
            vertices.Add(vertex);
            revisions.Add(0);
            boundaries.Add(BoundaryState.Unknown);
            updates.Advance(adjacencyRecords + (long)index + 1);
        }
        updates.Complete();
        updates.Start(SimplificationPhase.Triangles, sourceMesh.Faces.Count, "triangles");
        IList<FaceVertices> faces = work?.List(new FaceVerticesCodec()) ?? new List<FaceVertices>(sourceMesh.Faces.Count);
        IList<bool> removed = work?.List(new BoolRecordCodec()) ?? new bool[sourceMesh.Faces.Count];
        foreach (var face in SimplificationGeometry.Faces(sourceMesh))
        {
            cancellation.ThrowIfCancellationRequested();
            faces.Add(face);
            if (work != null)
            {
                removed.Add(false);
            }
            updates.Advance(faces.Count);
        }
        updates.Complete();
        updates.Start(SimplificationPhase.Quadrics, faces.Count, "triangles");
        adjacency.Initialize(faces, cancellation);
        var remaining = faces.Count;
        Span<double> plane = stackalloc double[4];
        for (var i = 0; i < faces.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            updates.Advance(i);
            var f = faces[i];
            if (initialVertices != null)
            {
                continue;
            }

            var a = vertices[f[0]].Position;
            var normal = (vertices[f[1]].Position - a).Cross(vertices[f[2]].Position - a).Unit;
            plane[0] = normal.X;
            plane[1] = normal.Y;
            plane[2] = normal.Z;
            plane[3] = -(normal).Dot(a);
            for (var corner = 0; corner < 3; corner++)
            {
                var v = f[corner];
                var vertex = vertices[v];
                vertex.PlaneCount++;
                for (var r = 0; r < 4; r++)
                {
                    for (var c = r; c < 4; c++)
                    {
                        vertex[r * 4 + c] += plane[r] * plane[c];
                    }
                }
                vertices[v] = vertex;
            }
        }

        updates.Complete();
        var queue = new CollapseCandidateQueue(faces.Count, work, cancellation);
        var edgeCounts = new Dictionary<int, int>();
        var boundaryNeighbours = new HashSet<int>();
        void Neighbours(int vertex, HashSet<int> result)
        {
            result.Clear();
            var measureBoundary = boundaries[vertex] == BoundaryState.Unknown;
            if (measureBoundary)
            {
                edgeCounts.Clear();
            }
            foreach (var face in adjacency.Faces(vertex))
            {
                var triangleVertices = faces[face];
                for (var corner = 0; corner < 3; corner++)
                {
                    var neighbour = triangleVertices[corner];
                    if (neighbour != vertex)
                    {
                        result.Add(neighbour);
                        if (measureBoundary)
                        {
                            edgeCounts.TryGetValue(neighbour, out var count);
                            edgeCounts[neighbour] = count + 1;
                        }
                    }
                }
            }
            if (measureBoundary)
            {
                boundaries[vertex] = edgeCounts.Values.Contains(1) ? BoundaryState.Boundary : BoundaryState.Interior;
            }
        }
        bool Boundary(int vertex)
        {
            var cached = boundaries[vertex];
            if (cached != BoundaryState.Unknown)
            {
                return cached == BoundaryState.Boundary;
            }
            Neighbours(vertex, boundaryNeighbours);
            return boundaries[vertex] == BoundaryState.Boundary;
        }
        void Offer(int a, int b)
        {
            if (a == b)
            {
                return;
            }

            if (lockedVertices != null && (lockedVertices[a] || lockedVertices[b]))
            {
                return;
            }

            if (a > b)
            {
                var t = a;
                a = b;
                b = t;
            }

            if (tolerances != null && (tolerances[a] == 0 || tolerances[b] == 0))
            {
                return;
            }
            var va = vertices[a];
            var vb = vertices[b];
            if (va.Removed || vb.Removed)
            {
                return;
            }
            var aBoundary = Boundary(a);
            var bBoundary = Boundary(b);
            Span<V3> options = stackalloc V3[3];
            var optionCount = 1;
            if (aBoundary && !bBoundary)
            {
                options[0] = va.Position;
            }
            else if (bBoundary && !aBoundary)
            {
                options[0] = vb.Position;
            }
            else
            {
                options[0] = va.Position;
                options[1] = vb.Position;
                options[2] = (va.Position + vb.Position) * .5;
                optionCount = 3;
            }
            var best = double.PositiveInfinity;
            var bestPoint = options[0];
            var quadric = new EdgeQuadric(va, vb);
            Span<double> errors = stackalloc double[3];
            quadric.Evaluate(options[..optionCount], errors);
            for (var optionIndex = 0; optionIndex < optionCount; optionIndex++)
            {
                var option = options[optionIndex];
                var error = errors[optionIndex];
                error = Math.Max(0, error) / (va.PlaneCount + vb.PlaneCount);
                if (error < best)
                {
                    best = error;
                    bestPoint = option;
                }
            }

            var priority = best + (va.Position - vb.Position).Length * 1e-8;
            if (remaining <= targetFaceCount && !WithinTolerance(a, b, best))
            {
                queue.Remove(a, b);
                return;
            }
            queue.Offer(new Candidate(a, b, revisions[a], revisions[b], bestPoint, best), priority);
        }

        bool WithinTolerance(int a, int b, double error)
        {
            var tolerance = tolerances == null ? detailTolerance : Math.Min(tolerances[a], tolerances[b]);
            return tolerance > 0 && error <= tolerance * tolerance;
        }

        updates.Start(SimplificationPhase.Edges, faces.Count, "triangles");
        var indexedFaces = 0;
        using var edgeScratch = work?.CreateScope();
        var edges = new InitialEdgeIndex(faces.Count, edgeScratch, cancellation);
        foreach (var f in faces)
        {
            cancellation.ThrowIfCancellationRequested();
            updates.Advance(indexedFaces++);
            for (var i = 0; i < 3; i++)
            {
                var a = f[i];
                var b = f[(i + 1) % 3];
                edges.Add(a, b);
            }
        }

        updates.Complete();
        updates.Start(SimplificationPhase.Boundaries, edges.Count, "edges");
        var constrainedEdges = 0;
        // Constrain open boundaries in their own perpendicular planes. On a flat
        // patch, face quadrics alone have zero cost for cutting away its corners.
        foreach (var edge in edges.Edges())
        {
            cancellation.ThrowIfCancellationRequested();
            updates.Advance(constrainedEdges++);
            if (initialVertices != null)
            {
                continue;
            }
            var sharedCount = 0;
            var sharedFace = -1;
            foreach (var face in adjacency.Faces(edge.A))
            {
                if (faces[face].Contains(edge.B))
                {
                    sharedCount++;
                    sharedFace = face;
                }
            }
            if (sharedCount != 1)
            {
                continue;
            }

            var vertexA = vertices[edge.A];
            var vertexB = vertices[edge.B];
            var f = faces[sharedFace];
            var a = vertexA.Position;
            var b = vertexB.Position;
            var origin = vertices[f[0]].Position;
            var faceNormal = (vertices[f[1]].Position - origin).Cross(vertices[f[2]].Position - origin).Unit;
            var normal = (b - a).Cross(faceNormal).Unit;
            plane[0] = normal.X;
            plane[1] = normal.Y;
            plane[2] = normal.Z;
            plane[3] = -(normal).Dot(a);
            for (var endpoint = 0; endpoint < 2; endpoint++)
            {
                var vi = endpoint == 0 ? edge.A : edge.B;
                var vertex = endpoint == 0 ? vertexA : vertexB;
                for (var r = 0; r < 4; r++)
                {
                    for (var c = r; c < 4; c++)
                    {
                        vertex[r * 4 + c] += 100 * plane[r] * plane[c];
                    }
                }
                vertices[vi] = vertex;
            }
        }

        updates.Complete();
        updates.Start(SimplificationPhase.Candidates, edges.Count, "edges");
        var rankedEdges = 0;
        foreach (var edge in edges.Edges())
        {
            cancellation.ThrowIfCancellationRequested();
            Offer(edge.A, edge.B);
            updates.Advance(++rankedEdges);
        }
        updates.Complete();
        edgeScratch?.Dispose();
        updates.Start(SimplificationPhase.Collapse, null, "candidates");

        var commonFaces = new List<int>(3);
        var neighboursA = new HashSet<int>();
        var neighboursB = new HashSet<int>();
        var affected = new List<int>();
        var affectedSet = new HashSet<int>();
        var touched = new HashSet<int>();
        var updatedNeighbours = new HashSet<int>();
        var survivingTriangles = new HashSet<TriangleKey>();
        var costChanged = new HashSet<int>();
        var neighbourhoods = new List<CollapseNeighbourhood>();
        Span<int> triangle = stackalloc int[3];
        Span<V3> old = stackalloc V3[3];
        Span<V3> next = stackalloc V3[3];
        // A face ceiling alone leaves uniformly tessellated meshes untouched.
        // Continue below it while RMS distance to the accumulated source planes
        // is small. Normalizing by plane count keeps the tolerance in distance
        // units instead of growing with the number of contributing planes.
        var evaluated = 0L;
        var targetOnly = detailTolerance == 0 && tolerances == null;
        var filteredForTolerance = false;
        bool Current(Candidate candidate)
        {
            // Check compact revision stamps before decoding full vertices.
            // Candidates rejected by Offer still need this topology guard.
            return revisions[candidate.A] == candidate.RevisionA && revisions[candidate.B] == candidate.RevisionB;
        }
        while (!targetOnly || remaining > targetFaceCount)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!filteredForTolerance && remaining <= targetFaceCount)
            {
                // Discard the ineligible tail in one linear pass instead of
                // performing a heap removal for every rejected edge.
                queue.Retain(candidate => Current(candidate) && WithinTolerance(candidate.A, candidate.B, candidate.Error), cancellation);
                filteredForTolerance = true;
            }
            if (!queue.TryDequeue(out var edge))
            {
                break;
            }
            updates.Advance(++evaluated, remaining, queue.Count);
            if (!Current(edge))
            {
                continue;
            }
            if (remaining <= targetFaceCount && !WithinTolerance(edge.A, edge.B, edge.Error))
            {
                continue;
            }
            var a = vertices[edge.A];
            var b = vertices[edge.B];

            commonFaces.Clear();
            foreach (var face in adjacency.Faces(a.Id))
            {
                if (faces[face].Contains(b.Id))
                {
                    commonFaces.Add(face);
                }
            }
            if (commonFaces.Count is < 1 or > 2)
            {
                continue;
            }

            Neighbours(edge.A, neighboursA);
            Neighbours(edge.B, neighboursB);
            var sharedNeighbours = 0;
            foreach (var neighbour in neighboursA)
            {
                if (neighboursB.Contains(neighbour))
                {
                    sharedNeighbours++;
                }
            }
            if (sharedNeighbours != commonFaces.Count)
            {
                continue;
            }

            if (Boundary(a.Id) && Boundary(b.Id) && commonFaces.Count != 1)
            {
                continue;
            }

            affected.Clear();
            affectedSet.Clear();
            foreach (var face in adjacency.Faces(a.Id))
            {
                affected.Add(face);
                affectedSet.Add(face);
            }
            foreach (var face in adjacency.Faces(b.Id))
            {
                if (affectedSet.Add(face))
                {
                    affected.Add(face);
                }
            }
            var valid = true;
            survivingTriangles.Clear();
            foreach (var fi in affected)
            {
                if (commonFaces.Contains(fi))
                {
                    continue;
                }

                var face = faces[fi];
                for (var corner = 0; corner < 3; corner++)
                {
                    var vertex = face[corner];
                    old[corner] = vertices[vertex].Position;
                    next[corner] = vertex == edge.A || vertex == edge.B ? edge.Position : old[corner];
                    triangle[corner] = vertex == edge.B ? edge.A : vertex;
                }
                triangle.Sort();
                if (!survivingTriangles.Add(new TriangleKey(triangle[0], triangle[1], triangle[2])))
                {
                    valid = false;
                    break;
                }
                var oldNormal = (old[1] - old[0]).Cross(old[2] - old[0]);
                var newNormal = (next[1] - next[0]).Cross(next[2] - next[0]);
                if (newNormal.Length < 1e-10 || (oldNormal.Unit).Dot(newNormal.Unit) < .25)
                {
                    valid = false;
                    break;
                }
            }

            if (!valid)
            {
                continue;
            }

            touched.Clear();
            foreach (var face in affected)
            {
                var triangleVertices = faces[face];
                for (var corner = 0; corner < 3; corner++)
                {
                    touched.Add(triangleVertices[corner]);
                }
            }
            foreach (var fi in affected)
            {
                var original = faces[fi];
                if (commonFaces.Contains(fi))
                {
                    for (var corner = 0; corner < 3; corner++)
                    {
                        adjacency.Remove(original[corner], fi);
                    }
                    removed[fi] = true;
                    remaining--;
                    continue;
                }

                // Removing and immediately re-adding an unchanged corner only
                // returns its same adjacency slot. Preserve it in place; only
                // the removed endpoint transfers its face to the survivor.
                if (original.Contains(edge.B))
                {
                    adjacency.Remove(edge.B, fi);
                    faces[fi] = original.Replace(edge.B, edge.A);
                    adjacency.Add(edge.A, fi);
                }
            }

            if (tolerances != null)
            {
                tolerances[edge.A] = Math.Min(tolerances[edge.A], tolerances[edge.B]);
            }
            a.Position = edge.Position;
            a.PlaneCount += b.PlaneCount;
            for (var row = 0; row < 4; row++)
            {
                for (var column = row; column < 4; column++)
                {
                    a[row * 4 + column] += b[row * 4 + column];
                }
            }

            b.Removed = true;
            vertices[edge.A] = a;
            vertices[edge.B] = b;
            foreach (var vi in touched)
            {
                revisions[vi] = vi == edge.B ? -1 : revisions[vi] + 1;
            }

            // Edges incident to the removed vertex no longer exist. Also drop
            // surviving-endpoint edges whose last face was removed by this cut.
            foreach (var neighbour in neighboursB)
            {
                queue.Remove(edge.B, neighbour);
            }
            Neighbours(edge.A, updatedNeighbours);
            foreach (var neighbour in neighboursA)
            {
                if (!updatedNeighbours.Contains(neighbour))
                {
                    queue.Remove(edge.A, neighbour);
                }
            }

            costChanged.Clear();
            costChanged.Add(edge.A);
            var neighbourhoodCount = 0;
            foreach (var vi in touched)
            {
                if (vi == edge.B)
                {
                    continue;
                }
                if (neighbourhoodCount == neighbourhoods.Count)
                {
                    neighbourhoods.Add(new CollapseNeighbourhood());
                }
                var neighbourhood = neighbourhoods[neighbourhoodCount++];
                neighbourhood.Vertex = vi;
                var previousBoundary = boundaries[vi];
                boundaries[vi] = BoundaryState.Unknown;
                Neighbours(vi, neighbourhood.Neighbours);
                if (previousBoundary != boundaries[vi])
                {
                    costChanged.Add(vi);
                }
            }
            for (var index = 0; index < neighbourhoodCount; index++)
            {
                var neighbourhood = neighbourhoods[index];
                var vi = neighbourhood.Vertex;
                foreach (var n in neighbourhood.Neighbours)
                {
                    if (vi < n || !touched.Contains(n))
                    {
                        var unchangedCost = !costChanged.Contains(vi) && !costChanged.Contains(n);
                        if (!unchangedCost || !queue.Refresh(vi, n, revisions[vi], revisions[n]))
                        {
                            Offer(vi, n);
                        }
                    }
                }
            }
        }

        updates.Complete(remaining);
        updates.Start(SimplificationPhase.Output, faces.Count, "triangles");
        var result = outputWork?.Surface() ?? new Surface();
        IList<int> remap = work?.Filled(vertices.Count, -1, new IntRecordCodec()) ?? Enumerable.Repeat(-1, vertices.Count).ToArray();
        for (var i = 0; i < faces.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            updates.Advance(i);
            if (!removed[i])
            {
                var output = new int[3];
                var face = faces[i];
                for (var j = 0; j < 3; j++)
                {
                    var v = face[j];
                    var index = remap[v];
                    if (index < 0)
                    {
                        index = result.Vertices.Count;
                        remap[v] = index;
                        result.Vertices.Add(vertices[v].Position.Array);
                        outputVertices?.Add(v);
                        outputStates?.Add(vertices[v]);
                        if (outputTolerances != null)
                        {
                            outputTolerances.Add(tolerances![v]);
                        }
                    }

                    output[j] = index;
                }

                result.Faces.Add(output);
            }
        }

        updates.Complete(result.Faces.Count);
        return result;
    }
}
