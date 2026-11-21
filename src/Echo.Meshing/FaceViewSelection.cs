using Echo.Models;
using Echo.Compute;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Meshing;

internal static class FaceViewSelection
{
    public static int[] Select(Surface mesh, IReadOnlyList<DepthView> views, IReadOnlyDictionary<string, Photo> photos,
        CancellationToken cancellation, Action<string>? progress = null, PhotographSelectionProgress? selectionProgress = null, WorkBuffers? work = null,
        IComputeService? compute = null, ComputeBackend backend = ComputeBackend.Auto, bool optimizeSeams = true)
    {
        using var scratch = work?.CreateScope();
        var reporter = selectionProgress ?? new PhotographSelectionProgress(progress);
        using var candidateWork = scratch?.CreateScope(preferResident: true);
        var candidates = new FaceViewCandidateStore(mesh.Faces.Count, candidateWork, cancellation);
        var scales = new double[mesh.Faces.Count];
        var selected = new int[mesh.Faces.Count];
        Array.Fill(selected, -1);
        reporter.Start(PhotographSelectionPhase.BuildVisibilityIndex);
        using var visibility = new SurfaceTriangleIndex(mesh, cancellation, scratch);
        var sourcePhotos = views.Select(view => photos[view.Camera.ImageId]).ToArray();
        using (var occlusion = SurfaceOcclusionPreparation.Open(compute, backend, visibility, cancellation))
        {
            reporter.Start(PhotographSelectionPhase.CheckMeasuredVisibility, mesh.Faces.Count);
            FaceViewEvaluation.Evaluate(visibility, occlusion, views, sourcePhotos, candidates, scales, selected,
                false, reporter, cancellation, scratch);
            reporter.Complete();
            var missing = selected.Count(view => view < 0);
            reporter.Start(PhotographSelectionPhase.CheckCompletedGaps, views.Count > 0 ? missing : 0);
            if (missing > 0 && views.Count > 0)
            {
                FaceViewEvaluation.Evaluate(visibility, occlusion, views, sourcePhotos, candidates, scales, selected,
                    true, reporter, cancellation, scratch);
            }
            reporter.Complete();
        }

        if (!optimizeSeams)
        {
            return selected;
        }
        reporter.Start(PhotographSelectionPhase.BuildSeamNeighbours);
        var neighbours = SurfaceTopologyBuilder.Build(mesh, cancellation, scratch).Neighbours;
        IList<double> penalties = candidateWork?.Filled(neighbours.Count, 0d, new DoubleRecordCodec(), cancellation) ?? new double[neighbours.Count];
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
        FaceViewOptimizer.Optimize(candidates, selected, neighbours, penalties, views.Count, cancellation, message => reporter.Update(message), candidateWork);
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
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (candidate.Density >= bestDensity * .81)
            {
                candidates[retained++] = candidate;
            }
        }
        candidates.RemoveRange(retained, candidates.Count - retained);
        FaceViewCandidateRanking.RetainBest(candidates, 32);
    }

    internal static double Score(DepthView view, V3 a, V3 b, V3 c, V3 centre, double density)
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
