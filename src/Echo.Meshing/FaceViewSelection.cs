using Echo.Contracts;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Meshing;

internal static class FaceViewSelection
{
    public static int[] Select(Surface mesh, IReadOnlyList<DepthView> views, IReadOnlyDictionary<string, Photo> photos,
        CancellationToken cancellation, Action<string>? progress = null, PhotographSelectionProgress? selectionProgress = null, WorkBuffers? work = null)
    {
        using var scratch = work?.CreateScope();
        var reporter = selectionProgress ?? new PhotographSelectionProgress(progress);
        var candidates = new FaceViewCandidate[mesh.Faces.Count][];
        var scales = new double[mesh.Faces.Count];
        var selected = new int[mesh.Faces.Count];
        Array.Fill(selected, -1);
        reporter.Start(PhotographSelectionPhase.BuildVisibilityIndex);
        using var visibility = new SurfaceTriangleIndex(mesh, cancellation);
        var sourcePhotos = views.Select(view => photos[view.Camera.ImageId]).ToArray();
        reporter.Start(PhotographSelectionPhase.CheckMeasuredVisibility, mesh.Faces.Count);
        Parallel.For(0, mesh.Faces.Count, new ParallelOptions
        {
            CancellationToken = cancellation,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
        }, () => new List<FaceViewCandidate>(views.Count), (index, _, options) =>
        {
            options.Clear();
            var triangle = visibility.Triangle(index);
            var a = triangle.A;
            var b = triangle.B;
            var c = triangle.C;
            var centre = (a + b + c) / 3;
            var cross = (b - a).Cross(c - a);
            var area = cross.Length;
            scales[index] = Math.Sqrt(area);
            var bestVisibleDensity = 0.0;
            for (var viewIndex = 0; viewIndex < views.Count; viewIndex++)
            {
                var view = views[viewIndex];
                var density = FaceViewResolution.Density(view, sourcePhotos[viewIndex], a, b, c, area);
                // RetainDetail would discard this view even if it were visible.
                // Only a verified visible photograph can raise the threshold.
                if (density < bestVisibleDensity * .81)
                {
                    continue;
                }
                var score = density > 0 ? Score(view, a, b, c, centre, density) : 0;
                if (!(score > 0) || visibility.HasOccluder(view.Centre, centre))
                {
                    continue;
                }
                bestVisibleDensity = Math.Max(bestVisibleDensity, density);
                Insert(options, new FaceViewCandidate(viewIndex, score, density));
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
            reporter.Increment();
            return options;
        }, _ => { });
        reporter.Complete();

        var missing = selected.Count(view => view < 0);
        reporter.Start(PhotographSelectionPhase.CheckCompletedGaps, views.Count > 0 ? missing : 0);
        if (missing > 0 && views.Count > 0)
        {
            Parallel.For(0, selected.Length, new ParallelOptions
            {
                CancellationToken = cancellation,
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
            }, () => new List<FaceViewCandidate>(views.Count), (index, _, options) =>
            {
                options.Clear();
                if (selected[index] >= 0)
                {
                    return options;
                }
                var triangle = visibility.Triangle(index);
                var a = triangle.A;
                var b = triangle.B;
                var c = triangle.C;
                var centre = (a + b + c) / 3;
                var area = (b - a).Cross(c - a).Length;
                var bestVisibleDensity = 0.0;
                for (var viewIndex = 0; viewIndex < views.Count; viewIndex++)
                {
                    var view = views[viewIndex];
                    var density = FaceViewResolution.Density(view, sourcePhotos[viewIndex], a, b, c, area);
                    if (density < bestVisibleDensity * .81)
                    {
                        continue;
                    }
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
                    bestVisibleDensity = Math.Max(bestVisibleDensity, density);
                    Insert(options, new FaceViewCandidate(viewIndex, density, density));
                }
                if (options.Count > 0)
                {
                    RetainDetail(options);
                    var best = options[0].Cost;
                    selected[index] = options[0].View;
                    for (var option = 0; option < options.Count; option++)
                    {
                        options[option] = options[option] with
                        {
                            Cost = -Math.Log(options[option].Cost / best)
                        };
                    }
                    candidates[index] = options.ToArray();
                }
                reporter.Increment();
                return options;
            }, _ => { });
        }
        reporter.Complete();

        reporter.Start(PhotographSelectionPhase.BuildSeamNeighbours);
        var neighbours = SurfaceTopologyBuilder.Build(mesh, cancellation, scratch).Neighbours;
        var penalties = new double[neighbours.Count];
        for (var index = 0; index < mesh.Faces.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var triangle = visibility.Triangle(index);
            ReadOnlySpan<V3> positions = [triangle.A, triangle.B, triangle.C];
            for (var corner = 0; corner < 3; corner++)
            {
                var slot = index * 3 + corner;
                var other = neighbours[slot];
                if (other < 0)
                {
                    continue;
                }
                var length = (positions[corner] - positions[(corner + 1) % 3]).Length;
                // Relief is already handled by visibility and the angular data
                // cost. Colour continuity should not disappear at every crease.
                penalties[slot] = .5 * Math.Clamp(2 * length / Math.Max(1e-12, scales[index] + scales[other]), .25, 2);
            }
            reporter.Update();
        }

        reporter.Start(PhotographSelectionPhase.ChooseCoherentPhotographs);
        FaceViewOptimizer.Optimize(candidates, selected, neighbours, penalties, views.Count, cancellation, message => reporter.Update(message));
        return selected;
    }

    internal static void RetainDetail(List<FaceViewCandidate> candidates)
    {
        // Keep at least 90% of the best available linear photographic resolution.
        // Seam regularisation can choose among comparable views, but cannot spend
        // most of the available image detail just to avoid a source transition.
        var bestDensity = 0.0;
        foreach (var candidate in candidates)
        {
            bestDensity = Math.Max(bestDensity, candidate.Density);
        }
        var retained = 0;
        for (var index = 0; index < candidates.Count && retained < 32; index++)
        {
            var candidate = candidates[index];
            if (candidate.Density >= bestDensity * .81)
            {
                candidates[retained++] = candidate;
            }
        }
        candidates.RemoveRange(retained, candidates.Count - retained);
    }

    private static void Insert(List<FaceViewCandidate> options, FaceViewCandidate candidate)
    {
        var insert = 0;
        while (insert < options.Count && options[insert].Cost >= candidate.Cost)
        {
            insert++;
        }
        options.Insert(insert, candidate);
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
