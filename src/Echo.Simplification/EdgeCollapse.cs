using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Simplification;

[Export(typeof(IEdgeCollapse)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class EdgeCollapse : IEdgeCollapse
{

    [ImportingConstructor]
    public EdgeCollapse()
    {
    }

    public Surface Reduce(Surface sourceMesh, int targetFaceCount, CancellationToken cancellation, double detailTolerance = 0, IReadOnlyList<double>? vertexTolerances = null)
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
        var tolerances = vertexTolerances?.ToArray();
        if (sourceMesh.Faces.Count <= targetFaceCount && detailTolerance == 0 && tolerances == null)
        {
            return sourceMesh;
        }

        var vertices = new Vertex[sourceMesh.Vertices.Count];
        for (var index = 0; index < vertices.Length; index++)
        {
            vertices[index] = new Vertex { Position = new V3(sourceMesh.Vertices[index]) };
        }
        var faces = new int[sourceMesh.Faces.Count][];
        for (var index = 0; index < faces.Length; index++)
        {
            faces[index] = (int[])sourceMesh.Faces[index].Clone();
        }
        var removed = new bool[faces.Length];
        var remaining = faces.Length;
        Span<double> plane = stackalloc double[4];
        for (var i = 0; i < faces.Length; i++)
        {
            var f = faces[i];
            foreach (var v in f)
            {
                vertices[v].Faces.Add(i);
            }

            var a = vertices[f[0]].Position;
            var normal = (vertices[f[1]].Position - a).Cross(vertices[f[2]].Position - a).Unit;
            plane[0] = normal.X;
            plane[1] = normal.Y;
            plane[2] = normal.Z;
            plane[3] = -(normal).Dot(a);
            foreach (var v in f)
            {
                vertices[v].PlaneCount++;
                for (var r = 0; r < 4; r++)
                {
                    for (var c = 0; c < 4; c++)
                    {
                        vertices[v].Quadric[r * 4 + c] += plane[r] * plane[c];
                    }
                }
            }
        }

        var queue = new PriorityQueue<Candidate, double>();
        var edgeCounts = new Dictionary<int, int>();
        void Neighbours(int vertex, HashSet<int> result)
        {
            result.Clear();
            foreach (var face in vertices[vertex].Faces)
            {
                foreach (var neighbour in faces[face])
                {
                    if (neighbour != vertex)
                    {
                        result.Add(neighbour);
                    }
                }
            }
        }
        bool Boundary(int vertex)
        {
            edgeCounts.Clear();
            foreach (var face in vertices[vertex].Faces)
            {
                foreach (var neighbour in faces[face])
                {
                    if (neighbour != vertex)
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
                    return true;
                }
            }
            return false;
        }
        void Offer(int a, int b)
        {
            if (a == b || vertices[a].Removed || vertices[b].Removed)
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
            var qa = va.Quadric;
            var qb = vb.Quadric;
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
                        error += p[r] * p[c] * (qa[r * 4 + c] + qb[r * 4 + c]);
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
            queue.Enqueue(new Candidate(a, b, va.Revision, vb.Revision, bestPoint, best), priority);
        }

        var edges = new HashSet<Edge>();
        foreach (var f in faces)
        {
            for (var i = 0; i < 3; i++)
            {
                var a = f[i];
                var b = f[(i + 1) % 3];
                edges.Add(new Edge(Math.Min(a, b), Math.Max(a, b)));
            }
        }

        // Constrain open boundaries in their own perpendicular planes. On a flat
        // patch, face quadrics alone have zero cost for cutting away its corners.
        foreach (var edge in edges)
        {
            var sharedCount = 0;
            var sharedFace = -1;
            foreach (var face in vertices[edge.A].Faces)
            {
                if (vertices[edge.B].Faces.Contains(face))
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
            var a = vertices[edge.A].Position;
            var b = vertices[edge.B].Position;
            var faceNormal = (vertices[f[1]].Position - vertices[f[0]].Position).Cross(vertices[f[2]].Position - vertices[f[0]].Position).Unit;
            var normal = (b - a).Cross(faceNormal).Unit;
            plane[0] = normal.X;
            plane[1] = normal.Y;
            plane[2] = normal.Z;
            plane[3] = -(normal).Dot(a);
            for (var endpoint = 0; endpoint < 2; endpoint++)
            {
                var vi = endpoint == 0 ? edge.A : edge.B;
                for (var r = 0; r < 4; r++)
                {
                    for (var c = 0; c < 4; c++)
                    {
                        vertices[vi].Quadric[r * 4 + c] += 100 * plane[r] * plane[c];
                    }
                }
            }
        }

        foreach (var edge in edges)
        {
            Offer(edge.A, edge.B);
        }

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
        while (queue.TryDequeue(out var edge, out _))
        {
            cancellation.ThrowIfCancellationRequested();
            var allowedError = tolerances == null ? maximumDetailError : Math.Pow(Math.Min(tolerances[edge.A], tolerances[edge.B]), 2);
            if (remaining <= targetFaceCount && (allowedError == 0 || edge.Error > allowedError))
            {
                continue;
            }
            var a = vertices[edge.A];
            var b = vertices[edge.B];
            if (a.Removed || b.Removed || a.Revision != edge.RevisionA || b.Revision != edge.RevisionB)
            {
                continue;
            }

            commonFaces.Clear();
            foreach (var face in a.Faces)
            {
                if (b.Faces.Contains(face))
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

            if (Boundary(edge.A) && Boundary(edge.B) && commonFaces.Count != 1)
            {
                continue;
            }

            affected.Clear();
            affectedSet.Clear();
            foreach (var face in a.Faces)
            {
                affected.Add(face);
                affectedSet.Add(face);
            }
            foreach (var face in b.Faces)
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
                foreach (var vertex in faces[face])
                {
                    touched.Add(vertex);
                }
            }
            foreach (var fi in affected)
            {
                foreach (var vi in faces[fi])
                {
                    vertices[vi].Faces.Remove(fi);
                }

                if (commonFaces.Contains(fi))
                {
                    removed[fi] = true;
                    remaining--;
                    continue;
                }

                for (var i = 0; i < 3; i++)
                {
                    if (faces[fi][i] == edge.B)
                    {
                        faces[fi][i] = edge.A;
                    }
                }

                foreach (var vi in faces[fi])
                {
                    vertices[vi].Faces.Add(fi);
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
                a.Quadric[i] += b.Quadric[i];
            }

            b.Removed = true;
            foreach (var vi in touched)
            {
                vertices[vi].Revision++;
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

        var result = new Surface();
        var remap = new Dictionary<int, int>();
        for (var i = 0; i < faces.Length; i++)
        {
            if (!removed[i])
            {
                var output = new int[3];
                for (var j = 0; j < 3; j++)
                {
                    var v = faces[i][j];
                    if (!remap.TryGetValue(v, out var index))
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

        return result;
    }
}
