using MathNet.Numerics.LinearAlgebra.Double;

namespace Echo.Geometry;

public static class SimilarityEstimator
{
    public static SimilarityTransform? Estimate(IReadOnlyList<PositionCorrespondence> positions, Random random,
        CancellationToken cancellation)
    {
        if (positions.Count < 3)
        {
            return null;
        }

        var sample = new PositionCorrespondence[3];
        var best = new List<PositionCorrespondence>();
        var maximumIterations = 1024;
        for (var iteration = 0; iteration < maximumIterations; iteration++)
        {
            cancellation.ThrowIfCancellationRequested();
            RandomSampleSelector.Select<PositionCorrespondence>(positions, random, sample);
            var transform = Fit(sample);
            if (transform == null)
            {
                continue;
            }

            var inliers = Inliers(positions, transform);
            if (inliers.Count <= best.Count)
            {
                continue;
            }

            best = inliers;
            var fraction = (double)best.Count / positions.Count;
            var miss = Math.Clamp(1 - Math.Pow(fraction, 3), 1e-12, 1 - 1e-12);
            var required = (int)Math.Min(1024, Math.Ceiling(Math.Log(.001) / Math.Log(miss)));
            maximumIterations = Math.Min(maximumIterations, Math.Max(64, required));
        }

        var result = Fit(best);
        if (result == null)
        {
            return null;
        }

        // Refit after consensus so the arbitrary visual frame is tied to all
        // consistent GPS positions, rather than to one noisy camera attitude.
        for (var pass = 0; pass < 2; pass++)
        {
            result = Fit(Inliers(positions, result)) ?? result;
        }

        return result;
    }

    private static List<PositionCorrespondence> Inliers(IReadOnlyList<PositionCorrespondence> positions,
        SimilarityTransform transform)
    {
        var result = new List<PositionCorrespondence>();
        foreach (var position in positions)
        {
            if ((transform.Apply(position.Source) - position.Target).Length <= position.Tolerance)
            {
                result.Add(position);
            }
        }

        return result;
    }

    private static SimilarityTransform? Fit(IReadOnlyList<PositionCorrespondence> positions)
    {
        if (positions.Count < 3)
        {
            return null;
        }

        var sourceMean = new V3();
        var targetMean = new V3();
        foreach (var position in positions)
        {
            sourceMean += position.Source;
            targetMean += position.Target;
        }

        sourceMean /= positions.Count;
        targetMean /= positions.Count;

        var covariance = DenseMatrix.Create(3, 3, 0);
        double sourceVariance = 0;
        foreach (var position in positions)
        {
            var source = position.Source - sourceMean;
            var target = position.Target - targetMean;
            sourceVariance += source.Dot(source);
            var a = source.Array;
            var b = target.Array;
            for (var row = 0; row < 3; row++)
            {
                for (var column = 0; column < 3; column++)
                {
                    covariance[row, column] += b[row] * a[column];
                }
            }
        }

        if (!(sourceVariance > 1e-12))
        {
            return null;
        }

        var decomposition = covariance.Svd(true);
        if (decomposition.S[1] <= decomposition.S[0] * 1e-8)
        {
            return null;
        }

        var sign = (decomposition.U * decomposition.VT).Determinant() < 0 ? -1.0 : 1.0;
        var correction = DenseMatrix.CreateIdentity(3);
        correction[2, 2] = sign;
        var rotation = (decomposition.U * correction * decomposition.VT).ToRowMajorArray();
        var scale = (decomposition.S[0] + decomposition.S[1] + sign * decomposition.S[2]) / sourceVariance;
        if (!(scale > 0) || !double.IsFinite(scale))
        {
            return null;
        }

        return new SimilarityTransform(scale, rotation,
            targetMean - CameraGeometry.Rotate(rotation, sourceMean) * scale);
    }
}
