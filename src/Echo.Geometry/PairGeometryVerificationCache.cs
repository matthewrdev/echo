using Echo.Contracts;

namespace Echo.Geometry;

public static class PairGeometryVerificationCache
{
    public static List<Match> Inliers(ImagePair pair, double threshold, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var fingerprint = PairVerificationFingerprint.Compute(pair, threshold, "bundle-cpu");
        if (pair.BundleVerification?.Fingerprint == fingerprint)
        {
            return pair.BundleVerification.Inliers;
        }
        var estimate = FundamentalMatrixEstimator.Estimate(pair.Matches, threshold, cancellation);
        var inliers = estimate?.Inliers ?? pair.Matches.Where(match => match.Manual).ToList();
        pair.BundleVerification = new PairGeometryVerification { Fingerprint = fingerprint, Inliers = inliers };
        return inliers;
    }
}
