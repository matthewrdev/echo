using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Geometry;
using SkiaSharp;

namespace Echo.Texturing;

[Export(typeof(ITexturePatchBuilder)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class TexturePatchBuilder : ITexturePatchBuilder
{
    private readonly ITextureChartOrienter orienter;

    [ImportingConstructor]
    public TexturePatchBuilder(ITextureChartOrienter orienter)
    {
        this.orienter = orienter;
    }

    public IReadOnlyList<TexturePatch> Build(ProjectData project, ReconstructionScene scene, Surface mesh, CancellationToken cancellation)
    {
        var minimumNormalAgreement = Math.Cos(project.Settings.TextureChartAngle * Math.PI / 180);
        var neighbours = Adjacency(mesh);
        var assigned = new bool[mesh.Faces.Count];
        var points = new V3[mesh.Vertices.Count];
        for (var index = 0; index < points.Length; index++)
        {
            points[index] = new V3(mesh.Vertices[index]);
        }
        var normals = new V3[mesh.Faces.Count];
        for (var index = 0; index < normals.Length; index++)
        {
            var face = mesh.Faces[index];
            normals[index] = (points[face[1]] - points[face[0]]).Cross(points[face[2]] - points[face[0]]).Unit;
        }
        var cells = new List<TextureCell>();
        var patches = new List<TexturePatch>();
        for (var seed = 0; seed < mesh.Faces.Count; seed++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (assigned[seed])
            {
                continue;
            }

            var face = mesh.Faces[seed];
            var origin = points[face[0]];
            var normal = normals[seed];
            var right = (points[face[1]] - origin).Unit;
            var down = normal.Cross(right).Unit;
            var cellSize = Math.Max(1e-4, (points[face[1]] - origin).Length * 4);
            var patch = new TexturePatch();
            var pending = new Queue<int>();
            var visited = new HashSet<int>();
            var occupied = new Dictionary<TextureCell, List<TextureTriangle>>();
            pending.Enqueue(seed);
            while (pending.TryDequeue(out var index) && patch.Faces.Count < project.Settings.TextureChartFaces)
            {
                if (assigned[index] || !visited.Add(index) || normal.Dot(normals[index]) < minimumNormalAgreement)
                {
                    continue;
                }

                var vertices = mesh.Faces[index];
                var projected = new SKPoint[3];
                for (var corner = 0; corner < 3; corner++)
                {
                    var local = points[vertices[corner]] - origin;
                    projected[corner] = new SKPoint((float)local.Dot(right), (float)local.Dot(down));
                }
                var triangle = new TextureTriangle
                {
                    Points = projected,
                    Bounds = new SKRect(Math.Min(projected[0].X, Math.Min(projected[1].X, projected[2].X)),
                        Math.Min(projected[0].Y, Math.Min(projected[1].Y, projected[2].Y)),
                        Math.Max(projected[0].X, Math.Max(projected[1].X, projected[2].X)),
                        Math.Max(projected[0].Y, Math.Max(projected[1].Y, projected[2].Y)))
                };
                FillCells(triangle.Bounds, cellSize, cells);
                if (Overlaps(cells, occupied, triangle))
                {
                    continue;
                }

                assigned[index] = true;
                patch.Faces.Add(index);
                patch.Triangles.Add(vertices);
                for (var corner = 0; corner < 3; corner++)
                {
                    patch.Coordinates[vertices[corner]] = [projected[corner].X, projected[corner].Y];
                    var adjacent = neighbours[index * 3 + corner];
                    if (adjacent >= 0 && !assigned[adjacent])
                    {
                        pending.Enqueue(adjacent);
                    }
                }

                foreach (var cell in cells)
                {
                    if (!occupied.TryGetValue(cell, out var bucket))
                    {
                        bucket = [];
                        occupied.Add(cell, bucket);
                    }

                    bucket.Add(triangle);
                }
            }

            if (patch.Faces.Count == 0)
            {
                throw new EchoException("Cannot unwrap a degenerate surface triangle.", 5);
            }

            orienter.Orient(patch);
            patches.Add(patch);
        }

        return patches.OrderByDescending(p => p.Bounds.Height).ToArray();
    }

    private int[] Adjacency(Surface mesh)
    {
        var positions = new Dictionary<V3, int>();
        var welded = new int[mesh.Vertices.Count];
        for (var index = 0; index < welded.Length; index++)
        {
            var point = new V3(mesh.Vertices[index]);
            if (!positions.TryGetValue(point, out var id))
            {
                id = positions.Count;
                positions.Add(point, id);
            }

            welded[index] = id;
        }

        var neighbours = new int[mesh.Faces.Count * 3];
        Array.Fill(neighbours, -1);
        var edges = new Dictionary<TextureEdge, int>();
        for (var index = 0; index < mesh.Faces.Count; index++)
        {
            var face = mesh.Faces[index];
            for (var corner = 0; corner < 3; corner++)
            {
                var a = welded[face[corner]];
                var b = welded[face[(corner + 1) % 3]];
                var edge = new TextureEdge(Math.Min(a, b), Math.Max(a, b));
                if (edges.TryGetValue(edge, out var other))
                {
                    neighbours[index * 3 + corner] = other / 3;
                    neighbours[other] = index;
                }
                else
                {
                    edges.Add(edge, index * 3 + corner);
                }
            }
        }

        return neighbours;
    }

    private bool Overlaps(List<TextureCell> cells, Dictionary<TextureCell, List<TextureTriangle>> occupied, TextureTriangle triangle)
    {
        foreach (var cell in cells)
        {
            if (!occupied.TryGetValue(cell, out var existing))
            {
                continue;
            }
            foreach (var candidate in existing)
            {
                if (triangle.Overlaps(candidate))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private void FillCells(SKRect bounds, double size, List<TextureCell> cells)
    {
        cells.Clear();
        for (var y = (int)Math.Floor(bounds.Top / size); y <= (int)Math.Floor(bounds.Bottom / size); y++)
        {
            for (var x = (int)Math.Floor(bounds.Left / size); x <= (int)Math.Floor(bounds.Right / size); x++)
            {
                cells.Add(new TextureCell(x, y));
            }
        }
    }
}
