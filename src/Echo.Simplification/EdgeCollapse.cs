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

    public Surface Reduce(Surface input, int target, CancellationToken cancellation)
    {
        if (input.Faces.Count <= target)
        {
            return input;
        }

        var vertices = input.Vertices.Select(p => new Vertex { Position = new V3(p) }).ToArray();
        var faces = input.Faces.Select(f => (int[])f.Clone()).ToArray();
        var removed = new bool[faces.Length];
        var remaining = faces.Length;
        for (var i = 0; i < faces.Length; i++)
        {
            var f = faces[i];
            foreach (var v in f)
            {
                vertices[v].Faces.Add(i);
            }

            var a = vertices[f[0]].Position;
            var normal = (vertices[f[1]].Position - a).Cross(vertices[f[2]].Position - a).Unit;
            double[] plane = [normal.X, normal.Y, normal.Z, -(normal).Dot(a)];
            foreach (var v in f)
            {
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
        HashSet<int> Neighbours(int v) => vertices[v].Faces.SelectMany(f => faces[f]).Where(i => i != v).ToHashSet();
        bool Boundary(int v) => Neighbours(v).Any(n => vertices[v].Faces.Count(f => faces[f].Contains(n)) == 1);
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

            var va = vertices[a];
            var vb = vertices[b];
            var qa = va.Quadric;
            var qb = vb.Quadric;
            var aBoundary = Boundary(a);
            var bBoundary = Boundary(b);
            V3[] options = aBoundary && !bBoundary ? [va.Position] : bBoundary && !aBoundary ? [vb.Position] : [va.Position, vb.Position, (va.Position + vb.Position) * .5];
            var best = double.PositiveInfinity;
            var bestPoint = options[0];
            foreach (var option in options)
            {
                double[] p = [option.X, option.Y, option.Z, 1];
                double error = 0;
                for (var r = 0; r < 4; r++)
                {
                    for (var c = 0; c < 4; c++)
                    {
                        error += p[r] * p[c] * (qa[r * 4 + c] + qb[r * 4 + c]);
                    }
                }

                error += (va.Position - vb.Position).Length * 1e-8;
                if (error < best)
                {
                    best = error;
                    bestPoint = option;
                }
            }

            queue.Enqueue(new Candidate(a, b, va.Revision, vb.Revision, bestPoint), best);
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
            var shared = vertices[edge.A].Faces.Intersect(vertices[edge.B].Faces).ToArray();
            if (shared.Length != 1)
            {
                continue;
            }

            var f = faces[shared[0]];
            var a = vertices[edge.A].Position;
            var b = vertices[edge.B].Position;
            var faceNormal = (vertices[f[1]].Position - vertices[f[0]].Position).Cross(vertices[f[2]].Position - vertices[f[0]].Position).Unit;
            var normal = (b - a).Cross(faceNormal).Unit;
            double[] plane = [normal.X, normal.Y, normal.Z, -(normal).Dot(a)];
            foreach (var vi in new[]
            {
                edge.A,
                edge.B
            }
            )
            {
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

        while (remaining > target && queue.TryDequeue(out var edge, out _))
        {
            cancellation.ThrowIfCancellationRequested();
            var a = vertices[edge.A];
            var b = vertices[edge.B];
            if (a.Removed || b.Removed || a.Revision != edge.RevisionA || b.Revision != edge.RevisionB)
            {
                continue;
            }

            var commonFaces = a.Faces.Intersect(b.Faces).ToArray();
            if (commonFaces.Length is < 1 or > 2)
            {
                continue;
            }

            var neighboursA = Neighbours(edge.A);
            var neighboursB = Neighbours(edge.B);
            if (neighboursA.Intersect(neighboursB).Count() != commonFaces.Length)
            {
                continue;
            }

            if (Boundary(edge.A) && Boundary(edge.B) && commonFaces.Length != 1)
            {
                continue;
            }

            var affected = a.Faces.Union(b.Faces).ToArray();
            var valid = true;
            foreach (var fi in affected)
            {
                if (commonFaces.Contains(fi))
                {
                    continue;
                }

                var face = faces[fi];
                var old = face.Select(v => vertices[v].Position).ToArray();
                var next = face.Select(v => v == edge.A || v == edge.B ? edge.Position : vertices[v].Position).ToArray();
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

            var touched = affected.SelectMany(f => faces[f]).ToHashSet();
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

            a.Position = edge.Position;
            for (var i = 0; i < 16; i++)
            {
                a.Quadric[i] += b.Quadric[i];
            }

            b.Removed = true;
            foreach (var vi in touched)
            {
                vertices[vi].Revision++;
            }

            foreach (var vi in touched.Where(v => !vertices[v].Removed))
            {
                foreach (var n in Neighbours(vi))
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
