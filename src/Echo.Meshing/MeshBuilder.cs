using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Meshing;

[Export(typeof(IMeshBuilder)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class MeshBuilder : IMeshBuilder
{
    private readonly Grid[] corners = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0), new(0, 0, 1), new(1, 0, 1), new(1, 1, 1), new(0, 1, 1)];
    private readonly int[][] edges = [[0, 1], [1, 2], [2, 3], [3, 0], [4, 5], [5, 6], [6, 7], [7, 4], [0, 4], [1, 5], [2, 6], [3, 7]];

    [ImportingConstructor]
    public MeshBuilder()
    {
    }

    public Surface Extract(FusedVolume volume, CancellationToken cancellation)
    {
        var samples = volume.Samples.ToDictionary(s => new Grid(s.X, s.Y, s.Z), s => s.Distance);
        var cells = new Dictionary<Grid, int>();
        var result = new Surface();
        Span<float> values = stackalloc float[8];
        Span<Grid> quad = stackalloc Grid[4];
        Span<int> indices = stackalloc int[4];
        foreach (var key in samples.Keys)
        {
            cancellation.ThrowIfCancellationRequested();
            var valid = true;
            var hasNegative = false;
            var hasPositive = false;
            for (var i = 0; i < 8; i++)
            {
                if (!samples.TryGetValue(key.Add(corners[i].X, corners[i].Y, corners[i].Z), out values[i]))
                {
                    valid = false;
                    break;
                }
                hasNegative |= values[i] < 0;
                hasPositive |= values[i] >= 0;
            }

            if (!valid || !hasNegative || !hasPositive)
            {
                continue;
            }

            var point = new V3();
            var count = 0;
            foreach (var edge in edges)
            {
                var a = edge[0];
                var b = edge[1];
                if ((values[a] < 0) == (values[b] < 0))
                {
                    continue;
                }

                var t = values[a] / (values[a] - values[b]);
                point += new V3(key.X + corners[a].X + (corners[b].X - corners[a].X) * t, key.Y + corners[a].Y + (corners[b].Y - corners[a].Y) * t, key.Z + corners[a].Z + (corners[b].Z - corners[a].Z) * t) * volume.VoxelSize;
                count++;
            }

            cells[key] = result.Vertices.Count;
            result.Vertices.Add((point / count).Array);
        }

        foreach (var key in samples.Keys)
        {
            cancellation.ThrowIfCancellationRequested();
            var value = samples[key];
            for (var axis = 0; axis < 3; axis++)
            {
                var next = key.Add(axis == 0 ? 1 : 0, axis == 1 ? 1 : 0, axis == 2 ? 1 : 0);
                if (!samples.TryGetValue(next, out var other) || (value < 0) == (other < 0))
                {
                    continue;
                }

                quad[0] = key;
                switch (axis)
                {
                    case 0:
                        quad[1] = key.Add(0, -1, 0);
                        quad[2] = key.Add(0, -1, -1);
                        quad[3] = key.Add(0, 0, -1);
                        break;
                    case 1:
                        quad[1] = key.Add(0, 0, -1);
                        quad[2] = key.Add(-1, 0, -1);
                        quad[3] = key.Add(-1, 0, 0);
                        break;
                    default:
                        quad[1] = key.Add(-1, 0, 0);
                        quad[2] = key.Add(-1, -1, 0);
                        quad[3] = key.Add(0, -1, 0);
                        break;
                }
                var complete = true;
                for (var i = 0; i < 4; i++)
                {
                    if (!cells.TryGetValue(quad[i], out indices[i]))
                    {
                        complete = false;
                        break;
                    }
                }
                if (!complete)
                {
                    continue;
                }
                if (value >= 0)
                {
                    indices.Reverse();
                }

                result.Faces.Add([indices[0], indices[1], indices[2]]);
                result.Faces.Add([indices[0], indices[2], indices[3]]);
            }
        }

        if (result.Faces.Count == 0)
        {
            throw new EchoException("No connected surface could be extracted from the fused cloud.", 5);
        }

        return result;
    }

    public Surface AssignCameras(ProjectData project,
        ReconstructionScene scene,
        IReadOnlyList<DepthMap> maps,
        Surface geometry,
        CancellationToken cancellation)
    {
        var photos = new Dictionary<string, Photo>(project.Images.Count, StringComparer.Ordinal);
        var cameras = new Dictionary<string, CameraPose>(scene.Cameras.Count, StringComparer.Ordinal);
        foreach (var photo in project.Images)
        {
            photos.Add(photo.Id, photo);
        }
        foreach (var camera in scene.Cameras)
        {
            cameras.Add(camera.ImageId, camera);
        }
        var views = new DepthView[maps.Count];
        for (var index = 0; index < maps.Count; index++)
        {
            var map = maps[index];
            views[index] = new DepthView(photos[map.ImageId], cameras[map.ImageId], map);
        }
        var result = new Surface();
        var vertices = new Dictionary<ChartVertex, int>();
        foreach (var face in geometry.Faces)
        {
            cancellation.ThrowIfCancellationRequested();
            var a = new V3(geometry.Vertices[face[0]]);
            var b = new V3(geometry.Vertices[face[1]]);
            var c = new V3(geometry.Vertices[face[2]]);
            var centre = (a + b + c) / 3;
            var normal = (b - a).Cross(c - a).Unit;
            DepthView? best = null;
            var bestScore = double.NegativeInfinity;
            foreach (var view in views)
            {
                var difference = view.Difference(centre);
                if (!double.IsFinite(difference))
                {
                    continue;
                }

                var direction = view.Centre - centre;
                var distance = direction.Length;
                if (Math.Abs(difference) > Math.Max(distance * .025, distance / view.Focal * 3))
                {
                    continue;
                }

                var photo = photos[view.Camera.ImageId];
                bool Inside(V3 point)
                {
                    var pixel = view.Projection.Project(point);
                    return pixel.HasValue && pixel.Value.X >= 1 && pixel.Value.Y >= 1 && pixel.Value.X < photo.Width - 1 && pixel.Value.Y < photo.Height - 1;
                }

                if (!Inside(a) || !Inside(b) || !Inside(c))
                {
                    continue;
                }

                var score = Math.Abs((normal).Dot(direction.Unit)) * view.Focal / distance / (1 + Math.Abs(difference));
                if (score > bestScore)
                {
                    bestScore = score;
                    best = view;
                }
            }

            if (best == null)
            {
                continue;
            }

            var output = new int[3];
            for (var i = 0; i < 3; i++)
            {
                var key = new ChartVertex(face[i], best.Camera.ImageId);
                if (!vertices.TryGetValue(key, out var index))
                {
                    index = result.Vertices.Count;
                    vertices[key] = index;
                    result.Vertices.Add(geometry.Vertices[face[i]]);
                }

                output[i] = index;
            }

            result.Faces.Add(output);
            result.SourceImages.Add(best.Camera.ImageId);
        }

        if (result.Faces.Count < 20)
        {
            throw new EchoException("Too little fused surface has a verified source photograph.", 5);
        }

        return result;
    }
}
