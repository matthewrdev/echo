using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Storage;
using Echo.Geometry;

namespace Echo.Simplification;

[Export(typeof(IEdgeCollapse)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class EdgeCollapse : IEdgeCollapse
{

    [ImportingConstructor]
    public EdgeCollapse()
    {
    }

    public Surface Reduce(Surface sourceMesh, int targetFaceCount, CancellationToken cancellation, double detailTolerance = 0, IReadOnlyList<double>? vertexTolerances = null, WorkBuffers? work = null, Action<OperationProgress>? progress = null)
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
        using var scratch = work?.CreateScope();
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
        if (sourceMesh.Faces.Count <= targetFaceCount && detailTolerance == 0 && tolerances == null)
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
        for (var index = 0; index < sourceMesh.Vertices.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            vertices.Add(new Vertex { Id = index, Position = new V3(sourceMesh.Vertices[index]) });
            revisions.Add(0);
            boundaries.Add(BoundaryState.Unknown);
            updates.Advance(adjacencyRecords + (long)index + 1);
        }
        updates.Complete();
        updates.Start(SimplificationPhase.Triangles, sourceMesh.Faces.Count, "triangles");
        IList<FaceVertices> faces = work?.List(new FaceVerticesCodec()) ?? new List<FaceVertices>(sourceMesh.Faces.Count);
        IList<bool> removed = work?.List(new BoolRecordCodec()) ?? new bool[sourceMesh.Faces.Count];
        foreach (var face in sourceMesh.Faces)
        {
            cancellation.ThrowIfCancellationRequested();
            faces.Add(new FaceVertices(face[0], face[1], face[2]));
            if (work != null)
            {
                removed.Add(false);
            }
            updates.Advance(faces.Count);
        }
        updates.Complete();
        updates.Start(SimplificationPhase.Quadrics, faces.Count, "triangles");
        var remaining = faces.Count;
        Span<double> plane = stackalloc double[4];
        for (var i = 0; i < faces.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            updates.Advance(i);
            var f = faces[i];
            for (var corner = 0; corner < 3; corner++)
            {
                adjacency.Add(f[corner], i);
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
                    for (var c = 0; c < 4; c++)
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
        void Neighbours(int vertex, HashSet<int> result)
        {
            result.Clear();
            foreach (var face in adjacency.Faces(vertex))
            {
                var triangleVertices = faces[face];
                for (var corner = 0; corner < 3; corner++)
                {
                    var neighbour = triangleVertices[corner];
                    if (neighbour != vertex)
                    {
                        result.Add(neighbour);
                    }
                }
            }
        }
        bool Boundary(Vertex vertex)
        {
            var cached = boundaries[vertex.Id];
            if (cached != BoundaryState.Unknown)
            {
                return cached == BoundaryState.Boundary;
            }
            edgeCounts.Clear();
            foreach (var face in adjacency.Faces(vertex.Id))
            {
                var triangleVertices = faces[face];
                for (var corner = 0; corner < 3; corner++)
                {
                    var neighbour = triangleVertices[corner];
                    if (neighbour != vertex.Id)
                    {
                        edgeCounts.TryGetValue(neighbour, out var count);
                        edgeCounts[neighbour] = count + 1;
                    }
                }
            }
            foreach (var count in edgeCounts.Values)
            {
                if (count == 1)
                {
                    boundaries[vertex.Id] = BoundaryState.Boundary;
                    return true;
                }
            }
            boundaries[vertex.Id] = BoundaryState.Interior;
            return false;
        }
        void Offer(int a, int b)
        {
            if (a == b)
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
            var aBoundary = Boundary(va);
            var bBoundary = Boundary(vb);
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
            Span<double> p = stackalloc double[4];
            p[3] = 1;
            foreach (var option in options[..optionCount])
            {
                p[0] = option.X;
                p[1] = option.Y;
                p[2] = option.Z;
                double error = 0;
                for (var r = 0; r < 4; r++)
                {
                    for (var c = 0; c < 4; c++)
                    {
                        error += p[r] * p[c] * (va[r * 4 + c] + vb[r * 4 + c]);
                    }
                }

                error = Math.Max(0, error) / (va.PlaneCount + vb.PlaneCount);
                if (error < best)
                {
                    best = error;
                    bestPoint = option;
                }
            }

            var priority = best + (va.Position - vb.Position).Length * 1e-8;
            queue.Offer(new Candidate(a, b, va.Revision, vb.Revision, bestPoint, best), priority);
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
            var vertexA = vertices[edge.A];
            var vertexB = vertices[edge.B];
            var sharedCount = 0;
            var sharedFace = -1;
            foreach (var face in adjacency.Faces(vertexA.Id))
            {
                if (adjacency.Contains(vertexB.Id, face))
                {
                    sharedCount++;
                    sharedFace = face;
                }
            }
            if (sharedCount != 1)
            {
                continue;
            }

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
                    for (var c = 0; c < 4; c++)
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
        Span<int> triangle = stackalloc int[3];
        Span<V3> old = stackalloc V3[3];
        Span<V3> next = stackalloc V3[3];
        // A face ceiling alone leaves uniformly tessellated meshes untouched.
        // Continue below it while RMS distance to the accumulated source planes
        // is small. Normalizing by plane count keeps the tolerance in distance
        // units instead of growing with the number of contributing planes.
        var maximumDetailError = detailTolerance * detailTolerance;
        var evaluated = 0L;
        var targetOnly = detailTolerance == 0 && tolerances == null;
        bool Current(Candidate candidate)
        {
            // Check compact revision stamps before decoding full vertices.
            // Candidates rejected by Offer still need this topology guard.
            return revisions[candidate.A] == candidate.RevisionA && revisions[candidate.B] == candidate.RevisionB;
        }
        while ((!targetOnly || remaining > targetFaceCount) && queue.TryDequeue(out var edge))
        {
            cancellation.ThrowIfCancellationRequested();
            updates.Advance(++evaluated, remaining, queue.Count);
            if (!Current(edge))
            {
                continue;
            }
            var allowedError = tolerances == null ? maximumDetailError : Math.Pow(Math.Min(tolerances[edge.A], tolerances[edge.B]), 2);
            if (remaining <= targetFaceCount && (allowedError == 0 || edge.Error > allowedError))
            {
                continue;
            }
            var a = vertices[edge.A];
            var b = vertices[edge.B];

            commonFaces.Clear();
            foreach (var face in adjacency.Faces(a.Id))
            {
                if (adjacency.Contains(b.Id, face))
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

            if (Boundary(a) && Boundary(b) && commonFaces.Count != 1)
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
                for (var corner = 0; corner < 3; corner++)
                {
                    adjacency.Remove(original[corner], fi);
                }

                if (commonFaces.Contains(fi))
                {
                    removed[fi] = true;
                    remaining--;
                    continue;
                }

                var updated = original.Replace(edge.B, edge.A);
                faces[fi] = updated;

                for (var corner = 0; corner < 3; corner++)
                {
                    adjacency.Add(updated[corner], fi);
                }
            }

            if (tolerances != null)
            {
                tolerances[edge.A] = Math.Min(tolerances[edge.A], tolerances[edge.B]);
            }
            a.Position = edge.Position;
            a.PlaneCount += b.PlaneCount;
            for (var i = 0; i < 16; i++)
            {
                a[i] += b[i];
            }

            b.Removed = true;
            vertices[edge.A] = a;
            vertices[edge.B] = b;
            foreach (var vi in touched)
            {
                var vertex = vertices[vi];
                vertex.Revision++;
                vertices[vi] = vertex;
                revisions[vi] = vertex.Removed ? -1 : vertex.Revision;
                boundaries[vi] = BoundaryState.Unknown;
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

            foreach (var vi in touched)
            {
                if (vertices[vi].Removed)
                {
                    continue;
                }
                Neighbours(vi, updatedNeighbours);
                foreach (var n in updatedNeighbours)
                {
                    if (vi < n || !touched.Contains(n))
                    {
                        Offer(vi, n);
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
