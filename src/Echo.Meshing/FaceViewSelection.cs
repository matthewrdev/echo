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
        var visibility = new SurfaceTriangleIndex(mesh, cancellation);
        var checkedFaces = 0;
        progress?.Invoke("Checking source visibility and detail in original photograph pixels…");
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
            scales[index] = Math.Sqrt(cross.Length);
            var options = new List<FaceViewCandidate>(33);
            for (var viewIndex = 0; viewIndex < views.Count; viewIndex++)
            {
                var view = views[viewIndex];
                var density = FaceViewResolution.Density(view, photos[view.Camera.ImageId], a, b, c);
                var score = density > 0 ? Score(view, a, b, c, centre, density) : 0;
                if (!(score > 0) || visibility.HasOccluder(view.Centre, centre))
                {
                    continue;
                }
                var insert = options.FindIndex(option => option.Cost < score);
                options.Insert(insert < 0 ? options.Count : insert, new FaceViewCandidate(viewIndex, score, density));
            }
            if (options.Count > 0)
            {
                RetainDetail(options);
                selected[index] = options[0].View;
                var best = options[0].Cost;
                for (var option = 0; option < options.Count; option++)
                {
                    options[option] = options[option] with
                    {
                        Cost = -Math.Log(options[option].Cost / best)
                    };
                }
            }
            candidates[index] = options.ToArray();
            var completed = Interlocked.Increment(ref checkedFaces);
            if (completed % 50000 == 0 || completed == mesh.Faces.Count)
            {
                progress?.Invoke($"Source photograph candidates: {completed:N0}/{mesh.Faces.Count:N0} triangles…");
            }
        });

        if (selected.Any(view => view < 0) && views.Count > 0)
        {
            progress?.Invoke("Checking geometric visibility across completed gaps…");
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
                var options = new List<FaceViewCandidate>();
                for (var viewIndex = 0; viewIndex < views.Count; viewIndex++)
                {
                    var view = views[viewIndex];
                    var photo = photos[view.Camera.ImageId];
                    var density = FaceViewResolution.Density(view, photo, a, b, c);
                    // Missing depth is not evidence of an occluder. For inferred
                    // patches, use the completed surface's line of sight instead.
                    // A contradictory measured depth never enters this fallback.
                    if (!(density > 0) || double.IsFinite(view.Difference(centre)))
                    {
                        continue;
                    }
                    if (visibility.HasOccluder(view.Centre, centre))
                    {
                        continue;
                    }
                    options.Add(new FaceViewCandidate(viewIndex, density, density));
                }
                if (options.Count > 0)
                {
                    options = options.OrderByDescending(option => option.Cost).ToList();
                    RetainDetail(options);
                    var best = options[0].Cost;
                    selected[index] = options[0].View;
                    candidates[index] = options.Select(option => option with { Cost = -Math.Log(option.Cost / best) }).ToArray();
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

    internal static void RetainDetail(List<FaceViewCandidate> candidates)
    {
        // Keep at least 90% of the best available linear photographic resolution.
        // Seam regularisation can choose among comparable views, but cannot spend
        // most of the available image detail just to avoid a source transition.
        var bestDensity = candidates.Max(candidate => candidate.Density);
        candidates.RemoveAll(candidate => candidate.Density < bestDensity * .81);
        if (candidates.Count > 32)
        {
            candidates.RemoveRange(32, candidates.Count - 32);
        }
    }

    private static double Score(DepthView view, V3 a, V3 b, V3 c, V3 centre, double density)
    {
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

        return confidence * density / (1 + .25 * Math.Abs(difference) / tolerance);
    }
}
