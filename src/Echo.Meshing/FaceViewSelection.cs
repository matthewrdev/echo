using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Meshing;

internal static class FaceViewSelection
{
    public static int[] Select(Surface mesh, IReadOnlyList<DepthView> views, IReadOnlyDictionary<string, Photo> photos,
        CancellationToken cancellation, Action<string>? progress = null)
    {
        var candidates = new FaceViewCandidate[mesh.Faces.Count][];
        var scales = new double[mesh.Faces.Count];
        var selected = new int[mesh.Faces.Count];
        Array.Fill(selected, -1);
        progress?.Invoke("Checking source photograph visibility and detail…");
        Parallel.For(0, mesh.Faces.Count, new ParallelOptions
        {
            CancellationToken = cancellation,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
        }, index =>
        {
            var face = mesh.Faces[index];
            var a = new V3(mesh.Vertices[face[0]]);
            var b = new V3(mesh.Vertices[face[1]]);
            var c = new V3(mesh.Vertices[face[2]]);
            var centre = (a + b + c) / 3;
            var cross = (b - a).Cross(c - a);
            var normal = cross.Unit;
            scales[index] = Math.Sqrt(cross.Length);
            var options = new List<FaceViewCandidate>(33);
            for (var viewIndex = 0; viewIndex < views.Count; viewIndex++)
            {
                var view = views[viewIndex];
                var score = Score(view, photos[view.Camera.ImageId], a, b, c, centre, normal);
                if (!(score > 0))
                {
                    continue;
                }
                var insert = options.FindIndex(option => option.Cost < score);
                options.Insert(insert < 0 ? options.Count : insert, new FaceViewCandidate(viewIndex, score));
                if (options.Count > 32)
                {
                    options.RemoveAt(32);
                }
            }
            if (options.Count > 0)
            {
                selected[index] = options[0].View;
                var best = options[0].Cost;
                options.RemoveAll(option => option.Cost < best * .5);
                for (var option = 0; option < options.Count; option++)
                {
                    options[option] = options[option] with
                    {
                        Cost = -Math.Log(options[option].Cost / best)
                    };
                }
            }
            candidates[index] = options.ToArray();
        });

        if (selected.Any(view => view < 0) && views.Count > 0)
        {
            progress?.Invoke("Checking geometric visibility across completed gaps…");
            var visibility = new SurfaceTriangleIndex(mesh, cancellation);
            Parallel.For(0, selected.Length, new ParallelOptions
            {
                CancellationToken = cancellation,
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
            }, index =>
            {
                if (selected[index] >= 0)
                {
                    return;
                }
                var face = mesh.Faces[index];
                var a = new V3(mesh.Vertices[face[0]]);
                var b = new V3(mesh.Vertices[face[1]]);
                var c = new V3(mesh.Vertices[face[2]]);
                var centre = (a + b + c) / 3;
                var normal = (b - a).Cross(c - a).Unit;
                var options = new List<FaceViewCandidate>();
                for (var viewIndex = 0; viewIndex < views.Count; viewIndex++)
                {
                    var view = views[viewIndex];
                    var photo = photos[view.Camera.ImageId];
                    var covered = Inside(view, photo, a) && Inside(view, photo, b) && Inside(view, photo, c);
                    // Missing depth is not evidence of an occluder. For inferred
                    // patches, use the completed surface's line of sight instead.
                    // A contradictory measured depth never enters this fallback.
                    if (!covered || double.IsFinite(view.Difference(centre)))
                    {
                        continue;
                    }
                    var direction = view.Centre - centre;
                    var score = Math.Abs(normal.Dot(direction.Unit)) * view.Focal / direction.Length;
                    if (!(score > 0) || visibility.HasOccluder(view.Centre, centre))
                    {
                        continue;
                    }
                    options.Add(new FaceViewCandidate(viewIndex, score));
                }
                if (options.Count > 0)
                {
                    options = options.OrderByDescending(option => option.Cost).Take(32).ToList();
                    var best = options[0].Cost;
                    selected[index] = options[0].View;
                    candidates[index] = options.Where(option => option.Cost >= best * .5)
                        .Select(option => option with { Cost = -Math.Log(option.Cost / best) }).ToArray();
                }
            });
        }

        var neighbours = SurfaceTopologyBuilder.Build(mesh, cancellation).Neighbours;
        var penalties = new double[neighbours.Length];
        for (var index = 0; index < mesh.Faces.Count; index++)
        {
            for (var corner = 0; corner < 3; corner++)
            {
                var slot = index * 3 + corner;
                var other = neighbours[slot];
                if (other < 0)
                {
                    continue;
                }
                var face = mesh.Faces[index];
                var length = (new V3(mesh.Vertices[face[corner]]) - new V3(mesh.Vertices[face[(corner + 1) % 3]])).Length;
                // Relief is already handled by visibility and the angular data
                // cost. Colour continuity should not disappear at every crease.
                penalties[slot] = .5 * Math.Clamp(2 * length / Math.Max(1e-12, scales[index] + scales[other]), .25, 2);
            }
        }

        FaceViewOptimizer.Optimize(candidates, selected, neighbours, penalties, views.Count, cancellation, progress);
        return selected;
    }

    private static double Score(DepthView view, Photo photo, V3 a, V3 b, V3 c, V3 centre, V3 normal)
    {
        var covered = Inside(view, photo, a) && Inside(view, photo, b) && Inside(view, photo, c);
        if (!covered)
        {
            return 0;
        }
        var direction = view.Centre - centre;
        var distance = direction.Length;
        var tolerance = Math.Max(distance * .025, distance / view.Focal * 3);
        var difference = view.Difference(centre);
        var confidence = 1.0;
        if (double.IsFinite(difference))
        {
            if (Math.Abs(difference) > tolerance)
            {
                return 0;
            }
        }
        else
        {
            // A repaired hole can have no depth at its centre. Permit a source
            // only when two boundary vertices agree with its measured depths.
            var support = 0;
            ReadOnlySpan<V3> boundary = [a, b, c];
            foreach (var point in boundary)
            {
                var residual = view.Difference(point);
                if (double.IsFinite(residual) && Math.Abs(residual) <= tolerance)
                {
                    support++;
                }
            }
            if (support < 2)
            {
                return 0;
            }
            difference = 0;
            confidence = .6;
        }

        return confidence * Math.Abs(normal.Dot(direction.Unit)) * view.Focal / distance / (1 + Math.Abs(difference));
    }

    private static bool Inside(DepthView view, Photo photo, V3 point)
    {
        var pixel = view.Projection.Project(point);
        if (!pixel.HasValue)
        {
            return false;
        }
        var insideX = pixel.Value.X >= 1 && pixel.Value.X < photo.Width - 1;
        var insideY = pixel.Value.Y >= 1 && pixel.Value.Y < photo.Height - 1;
        return insideX && insideY;
    }
}
