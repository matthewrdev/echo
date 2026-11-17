using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Meshing;

internal static class FaceViewEvaluation
{
    public static void Evaluate(SurfaceTriangleIndex visibility, SurfaceOcclusionCompute? occlusion,
        IReadOnlyList<DepthView> views, Photo[] photos, FaceViewCandidateStore candidates, double[] scales,
        int[] selected, bool fallback, PhotographSelectionProgress reporter, CancellationToken cancellation)
    {
        var batchSize = Math.Max(1, Math.Min(256, 4096 / Math.Max(1, views.Count)));
        var options = new FaceViewCandidate[checked(batchSize * views.Count)];
        var rays = new SurfaceRay[options.Length];
        var raySlots = new int[options.Length];
        var hidden = new bool[options.Length];
        var parallel = new ParallelOptions
        {
            CancellationToken = cancellation,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
        };
        var retained = new List<FaceViewCandidate>(views.Count);
        var triangles = new SurfaceTriangle[batchSize];
        var visibleViews = new List<int>(views.Count);
        for (var first = 0; first < selected.Length; first += batchSize)
        {
            cancellation.ThrowIfCancellationRequested();
            var count = Math.Min(batchSize, selected.Length - first);
            Array.Clear(options);
            var minimum = new V3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
            var maximum = minimum * -1;
            for (var local = 0; local < count; local++)
            {
                var triangle = visibility.Triangle(first + local);
                triangles[local] = triangle;
                var low = triangle.Minimum;
                var high = triangle.Maximum;
                minimum = new V3(Math.Min(minimum.X, low.X), Math.Min(minimum.Y, low.Y), Math.Min(minimum.Z, low.Z));
                maximum = new V3(Math.Max(maximum.X, high.X), Math.Max(maximum.Y, high.Y), Math.Max(maximum.Z, high.Z));
            }
            visibleViews.Clear();
            for (var view = 0; view < views.Count; view++)
            {
                if (views[view].Projection.MaySee(minimum, maximum))
                {
                    visibleViews.Add(view);
                }
            }
            Parallel.For(0, count, parallel, local =>
            {
                var face = first + local;
                if (fallback && selected[face] >= 0)
                {
                    return;
                }
                var triangle = triangles[local];
                var centre = (triangle.A + triangle.B + triangle.C) / 3;
                var area = (triangle.B - triangle.A).Cross(triangle.C - triangle.A).Length;
                scales[face] = Math.Sqrt(area);
                foreach (var view in visibleViews)
                {
                    var density = FaceViewResolution.Density(views[view], photos[view], triangle.A, triangle.B, triangle.C, area);
                    if (!(density > 0))
                    {
                        continue;
                    }
                    var score = fallback
                        ? double.IsFinite(views[view].Difference(centre)) ? 0 : density
                        : FaceViewSelection.Score(views[view], triangle.A, triangle.B, triangle.C, centre, density);
                    if (score > 0)
                    {
                        options[local * views.Count + view] = new FaceViewCandidate(view, score, density);
                    }
                }
            });
            var rayCount = 0;
            for (var local = 0; local < count; local++)
            {
                if (fallback && selected[first + local] >= 0)
                {
                    continue;
                }
                var triangle = triangles[local];
                var centre = (triangle.A + triangle.B + triangle.C) / 3;
                foreach (var view in visibleViews)
                {
                    var slot = local * views.Count + view;
                    if (options[slot].Cost > 0)
                    {
                        raySlots[rayCount] = slot;
                        rays[rayCount++] = new SurfaceRay(views[view].Centre, centre);
                    }
                }
            }
            if (occlusion != null)
            {
                occlusion.Evaluate(rays.AsSpan(0, rayCount), hidden.AsSpan(0, rayCount), cancellation);
            }
            else
            {
                Parallel.For(0, rayCount, parallel, ray => hidden[ray] = visibility.HasOccluder(rays[ray].Start, rays[ray].End));
            }
            for (var ray = 0; ray < rayCount; ray++)
            {
                if (hidden[ray])
                {
                    options[raySlots[ray]] = default;
                }
            }
            for (var local = 0; local < count; local++)
            {
                var face = first + local;
                if (fallback && selected[face] >= 0)
                {
                    continue;
                }
                retained.Clear();
                foreach (var view in visibleViews)
                {
                    var option = options[local * views.Count + view];
                    if (option.Cost > 0)
                    {
                        retained.Add(option);
                    }
                }
                FaceViewSelection.RetainDetail(retained);
                if (retained.Count > 0)
                {
                    selected[face] = retained[0].View;
                    var best = retained[0].Cost;
                    for (var option = 0; option < retained.Count; option++)
                    {
                        retained[option] = retained[option] with
                        {
                            Cost = -Math.Log(retained[option].Cost / best)
                        };
                    }
                }
                candidates.Set(face, retained);
                reporter.Increment();
            }
        }
    }
}
