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
        var photos = project.Images.ToDictionary(photo => photo.Id);
        var cameras = scene.Cameras.ToDictionary(camera => camera.ImageId);
        var projections = scene.Cameras.ToDictionary(camera => camera.ImageId,
            camera => new CameraProjector(photos[camera.ImageId], camera));
        var neighbours = SurfaceTopologyBuilder.Build(mesh, cancellation).Neighbours;
        var assigned = new bool[mesh.Faces.Count];
        var points = new V3[mesh.Vertices.Count];
        for (var index = 0; index < points.Length; index++)
        {
            points[index] = new V3(mesh.Vertices[index]);
        }
        var normals = new V3[mesh.Faces.Count];
        var areas = new double[mesh.Faces.Count];
        for (var index = 0; index < normals.Length; index++)
        {
            var face = mesh.Faces[index];
            var normal = (points[face[1]] - points[face[0]]).Cross(points[face[2]] - points[face[0]]);
            areas[index] = normal.Length;
            normals[index] = normal.Unit;
        }
        var patches = new List<TexturePatch>();
        var seeds = Enumerable.Range(0, mesh.Faces.Count).OrderByDescending(index => areas[index]);
        foreach (var seed in seeds)
        {
            cancellation.ThrowIfCancellationRequested();
            if (assigned[seed])
            {
                continue;
            }

            var face = mesh.Faces[seed];
            var origin = points[face[0]];
            var right = (points[face[1]] - origin).Unit;
            var tangent = new TextureChartProjection(null, origin, right, normals[seed].Cross(right), 1);
            var cellSize = Math.Max(1e-4, (points[face[1]] - origin).Length * 4);
            var patch = Grow(seed, tangent, cellSize, project.Settings.TextureChartFaces, minimumNormalAgreement,
                mesh, points, normals, areas, neighbours, assigned, cancellation);
            var image = mesh.SourceImages[seed];
            projections.TryGetValue(image, out var camera);
            var pixelOrigin = camera?.Project(origin);
            if (pixelOrigin.HasValue)
            {
                var scale = pixelOrigin.Value.Z / CameraGeometry.Focal(photos[image], cameras[image]);
                var photographic = new TextureChartProjection(camera, pixelOrigin.Value, default, default, scale);
                var alternative = Grow(seed, photographic, cellSize, project.Settings.TextureChartFaces, minimumNormalAgreement,
                    mesh, points, normals, areas, neighbours, assigned, cancellation);
                // Tangent planes retain texel density on flat regions. A source view
                // can unfold more of a curved region without adding chart boundaries.
                if (alternative.Faces.Count > patch.Faces.Count)
                {
                    patch = alternative;
                }
            }

            if (patch.Faces.Count == 0)
            {
                throw new EchoException("Cannot unwrap a degenerate surface triangle.", 5);
            }

            foreach (var index in patch.Faces)
            {
                assigned[index] = true;
            }
            orienter.Orient(patch);
            patches.Add(patch);
        }

        return patches.OrderByDescending(p => p.Bounds.Height).ToArray();
    }

    private TexturePatch Grow(int seed, TextureChartProjection projection, double cellSize, int maximumFaces,
        double minimumNormalAgreement, Surface mesh, V3[] points, V3[] normals, double[] areas,
        int[] neighbours, bool[] assigned, CancellationToken cancellation)
    {
        var patch = new TexturePatch();
        var pending = new Queue<int>();
        var visited = new HashSet<int>();
        var occupied = new Dictionary<TextureCell, List<TextureTriangle>>();
        var cells = new List<TextureCell>();
        var orientation = 0;
        pending.Enqueue(seed);
        while (pending.TryDequeue(out var index) && patch.Faces.Count < maximumFaces)
        {
            cancellation.ThrowIfCancellationRequested();
            var unavailable = assigned[index] || !visited.Add(index);
            if (unavailable)
            {
                continue;
            }

            var vertices = mesh.Faces[index];
            var projected = new SKPoint[3];
            var projectable = true;
            for (var corner = 0; corner < 3; corner++)
            {
                var coordinate = projection.Project(points[vertices[corner]]);
                if (!coordinate.HasValue)
                {
                    projectable = false;
                    break;
                }
                projected[corner] = new SKPoint((float)coordinate.Value.X, (float)coordinate.Value.Y);
            }
            if (!projectable)
            {
                continue;
            }

            var area = ((double)projected[1].X - projected[0].X) * ((double)projected[2].Y - projected[0].Y)
                - ((double)projected[1].Y - projected[0].Y) * ((double)projected[2].X - projected[0].X);
            var distorted = !double.IsFinite(area) || Math.Abs(area) < areas[index] * .1 || Math.Abs(area) > areas[index] * 10;
            if (distorted || (orientation != 0 && Math.Sign(area) != orientation))
            {
                continue;
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

            orientation = Math.Sign(area);
            patch.Faces.Add(index);
            patch.Triangles.Add(vertices);
            for (var corner = 0; corner < 3; corner++)
            {
                patch.Coordinates[vertices[corner]] = [projected[corner].X, projected[corner].Y];
                var adjacent = neighbours[index * 3 + corner];
                var connected = adjacent >= 0 && !assigned[adjacent]
                    && normals[index].Dot(normals[adjacent]) >= minimumNormalAgreement;
                if (connected)
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
        return patch;
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
