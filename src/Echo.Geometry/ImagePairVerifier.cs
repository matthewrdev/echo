using Echo.Contracts;

namespace Echo.Geometry;

public static class ImagePairVerifier
{
    public static ImagePair Verify(Photo leftPhoto, Photo rightPhoto, ImagePair candidates, CancellationToken cancellation)
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
        var estimate = FundamentalMatrixEstimator.Estimate(automatic, threshold, cancellation);
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
