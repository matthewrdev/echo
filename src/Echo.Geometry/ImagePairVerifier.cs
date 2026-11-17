using Echo.Contracts;
using Echo.Compute;
using System.Runtime.ExceptionServices;

namespace Echo.Geometry;

public static class ImagePairVerifier
{
    public static List<ImagePair> VerifyAll(IReadOnlyDictionary<string, Photo> photos, IReadOnlyList<ImagePair> pairs,
        int workers, CancellationToken cancellation, IComputeService? compute = null, ComputeBackend backend = ComputeBackend.Auto)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workers, 1);
        var verified = new ImagePair?[pairs.Count];
        var options = new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = cancellation };
        try
        {
            Parallel.For(0, pairs.Count, options, index =>
            {
                var pair = pairs[index];
                if (photos.TryGetValue(pair.A, out var left) && photos.TryGetValue(pair.B, out var right))
                {
                    verified[index] = Verify(left, right, pair, cancellation, compute, backend);
                }
            });
        }
        catch (AggregateException error)
        {
            cancellation.ThrowIfCancellationRequested();
            ExceptionDispatchInfo.Capture(error.GetBaseException()).Throw();
            throw;
        }
        var result = new List<ImagePair>(pairs.Count);
        foreach (var pair in verified)
        {
            if (pair != null)
            {
                result.Add(pair);
            }
        }
        return result;
    }

    public static ImagePair Verify(Photo leftPhoto, Photo rightPhoto, ImagePair candidates, CancellationToken cancellation, IComputeService? compute = null, ComputeBackend backend = ComputeBackend.Auto)
    {
        cancellation.ThrowIfCancellationRequested();
        var threshold = Math.Max(leftPhoto.Width, rightPhoto.Width) / 1000.0;
        var automatic = new List<Match>(candidates.Matches.Count);
        foreach (var match in candidates.Matches)
        {
            if (!match.Manual)
            {
                automatic.Add(match);
            }
        }
        var estimate = FundamentalMatrixEstimator.Estimate(automatic, threshold, cancellation, compute, backend);
        var inliers = estimate == null ? new HashSet<Match>() : new HashSet<Match>(estimate.Inliers);
        var verified = new ImagePair { A = candidates.A, B = candidates.B };
        foreach (var match in candidates.Matches)
        {
            // Manual constraints are explicit user input, not evidence of a successful geometric fit.
            match.GeometricInlier = match.Manual ? null : inliers.Contains(match);
            if (match.Manual || match.GeometricInlier == true)
            {
                verified.Matches.Add(match);
            }
        }
        candidates.Verification = new PairVerification
        {
            ThresholdPixels = threshold,
            CandidateCount = automatic.Count,
            InlierCount = inliers.Count
        };
        return verified;
    }
}
