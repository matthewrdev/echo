using Echo.Models;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Meshing;

internal static class FaceViewEvaluation
{
    public static void Evaluate(SurfaceTriangleIndex visibility, SurfaceOcclusionCompute? occlusion,
        IReadOnlyList<DepthView> views, Photo[] photos, FaceViewCandidateStore candidates, double[] scales,
        int[] selected, bool fallback, PhotographSelectionProgress reporter, CancellationToken cancellation, WorkBuffers? work = null)
    {
        var bytesPerFace = checked(Math.Max(1, views.Count) * 80L + 128);
        var batchSize = (int)Math.Max(1, Math.Min(256, 16L * 1024 * 1024 / bytesPerFace));
        using var reservation = Reserve(work, bytesPerFace, ref batchSize);
        var cullingSize = Math.Max(1, Math.Min(32, 4096 / Math.Max(1, views.Count)));
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
        var faceIndices = new int[batchSize];
        var visibleViews = Enumerable.Range(0, (batchSize + cullingSize - 1) / cullingSize)
            .Select(_ => new List<int>(views.Count)).ToArray();
        for (var first = 0; first < selected.Length;)
        {
            cancellation.ThrowIfCancellationRequested();
            var count = 0;
            while (first < selected.Length && count < batchSize)
            {
                var face = first++;
                if (!fallback || selected[face] < 0)
                {
                    faceIndices[count++] = face;
                }
            }
            if (count == 0)
            {
                continue;
            }
            Array.Clear(options);
            for (var group = 0; group * cullingSize < count; group++)
            {
                var minimum = new V3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
                var maximum = minimum * -1;
                var end = Math.Min(count, (group + 1) * cullingSize);
                for (var local = group * cullingSize; local < end; local++)
                {
                    var triangle = visibility.Triangle(faceIndices[local]);
                    triangles[local] = triangle;
                    var low = triangle.Minimum;
                    var high = triangle.Maximum;
                    minimum = new V3(Math.Min(minimum.X, low.X), Math.Min(minimum.Y, low.Y), Math.Min(minimum.Z, low.Z));
                    maximum = new V3(Math.Max(maximum.X, high.X), Math.Max(maximum.Y, high.Y), Math.Max(maximum.Z, high.Z));
                }
                visibleViews[group].Clear();
                for (var view = 0; view < views.Count; view++)
                {
                    if (views[view].Projection.MaySee(minimum, maximum))
                    {
                        visibleViews[group].Add(view);
                    }
                }
            }
            Parallel.For(0, count, parallel, local =>
            {
                var face = faceIndices[local];
                var triangle = triangles[local];
                var centre = (triangle.A + triangle.B + triangle.C) / 3;
                var area = (triangle.B - triangle.A).Cross(triangle.C - triangle.A).Length;
                scales[face] = Math.Sqrt(area);
                foreach (var view in visibleViews[local / cullingSize])
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
                var triangle = triangles[local];
                var centre = (triangle.A + triangle.B + triangle.C) / 3;
                foreach (var view in visibleViews[local / cullingSize])
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
                var face = faceIndices[local];
                retained.Clear();
                foreach (var view in visibleViews[local / cullingSize])
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
    private static PageMemoryReservation? Reserve(WorkBuffers? work, long bytesPerFace, ref int count)
    {
        if (work == null)
        {
            return null;
        }
        while (count > 1)
        {
            var reservation = work.TryReserveMemory(count * bytesPerFace);
            if (reservation != null)
            {
                return reservation;
            }
            count /= 2;
        }
        return work.TryReserveMemory(bytesPerFace);
    }

}
