using System.Buffers;
using System.Runtime.InteropServices;
using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Compute;
using Echo.Geometry;

namespace Echo.Matching;

[Export(typeof(IGuidedFeatureMatcher)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class GuidedFeatureMatcher : IGuidedFeatureMatcher
{

    private readonly IComputeService? compute;

    [ImportingConstructor]
    public GuidedFeatureMatcher([Import(AllowDefault = true)] IComputeService? compute = null)
    {
        this.compute = compute;
    }

    public ImagePair? Recover(FeatureSet left, FeatureSet right, IReadOnlyList<Match> initialMatches, Settings settings,
        double thresholdPixels, CancellationToken cancellation)
    {
        var seed = FundamentalMatrixEstimator.Estimate(initialMatches, thresholdPixels, cancellation, compute, settings.ComputeBackend);
        if (seed == null || seed.Inliers.Count < 20)
        {
            return null;
        }
        var candidates = Search(left, right, seed.Matrix, settings, thresholdPixels * 2, cancellation);
        // The seed stays independent of the guided search. New correspondences must
        // also survive a fresh robust model, rather than being accepted by the guide.
        var unique = new HashSet<MatchPixelKey>();
        foreach (var candidate in candidates)
        {
            unique.Add(Key(candidate));
        }
        foreach (var match in seed.Inliers)
        {
            if (unique.Add(Key(match)))
            {
                candidates.Add(match);
            }
        }
        var verified = FundamentalMatrixEstimator.Estimate(candidates, thresholdPixels, cancellation, compute, settings.ComputeBackend);
        if (verified == null || verified.Inliers.Count <= seed.Inliers.Count)
        {
            return null;
        }
        return new ImagePair { A = left.ImageId, B = right.ImageId, Matches = verified.Inliers };
    }

    private List<Match> Search(FeatureSet left, FeatureSet right, ReadOnlySpan<double> matrix, Settings settings,
        double thresholdPixels, CancellationToken cancellation)
    {
        var accelerated = GuidedDescriptorCompute.TrySearch(compute, left, right, matrix, settings, thresholdPixels, cancellation);
        if (accelerated != null)
        {
            return accelerated;
        }
        var leftCount = left.Features.Count;
        var rightCount = right.Features.Count;
        var distances = ArrayPool<double>.Shared.Rent(rightCount + leftCount * 2);
        var indices = ArrayPool<int>.Shared.Rent(rightCount + leftCount);
        try
        {
            var rightBest = distances.AsSpan(0, rightCount);
            var leftBest = distances.AsSpan(rightCount, leftCount);
            var leftSecond = distances.AsSpan(rightCount + leftCount, leftCount);
            var rightToLeft = indices.AsSpan(0, rightCount);
            var leftToRight = indices.AsSpan(rightCount, leftCount);
            rightBest.Fill(double.PositiveInfinity);
            leftBest.Fill(double.PositiveInfinity);
            leftSecond.Fill(double.PositiveInfinity);
            leftToRight.Fill(-1);
            rightToLeft.Fill(-1);
            for (var leftIndex = 0; leftIndex < leftCount; leftIndex++)
            {
                cancellation.ThrowIfCancellationRequested();
                var feature = left.Features[leftIndex];
                var lineX = matrix[0] * feature.X + matrix[1] * feature.Y + matrix[2];
                var lineY = matrix[3] * feature.X + matrix[4] * feature.Y + matrix[5];
                var lineZ = matrix[6] * feature.X + matrix[7] * feature.Y + matrix[8];
                var limit = thresholdPixels * Math.Sqrt(lineX * lineX + lineY * lineY);
                if (limit < 1e-15)
                {
                    continue;
                }
                for (var rightIndex = 0; rightIndex < rightCount; rightIndex++)
                {
                    var other = right.Features[rightIndex];
                    if (Math.Abs(lineX * other.X + lineY * other.Y + lineZ) > limit)
                    {
                        continue;
                    }
                    var distance = Distance(feature.DescriptorData.Span, other.DescriptorData.Span);
                    if (distance < rightBest[rightIndex])
                    {
                        rightBest[rightIndex] = distance;
                        rightToLeft[rightIndex] = leftIndex;
                    }
                    if (distance < leftBest[leftIndex])
                    {
                        leftSecond[leftIndex] = leftBest[leftIndex];
                        leftBest[leftIndex] = distance;
                        leftToRight[leftIndex] = rightIndex;
                    }
                    else if (distance < leftSecond[leftIndex])
                    {
                        leftSecond[leftIndex] = distance;
                    }
                }
            }
            var result = new List<Match>();
            for (var leftIndex = 0; leftIndex < leftCount; leftIndex++)
            {
                var rightIndex = leftToRight[leftIndex];
                if (rightIndex < 0 || rightToLeft[rightIndex] != leftIndex)
                {
                    continue;
                }
                var comparable = double.IsFinite(leftSecond[leftIndex]) || leftBest[leftIndex] <= settings.MatchDistanceThreshold * .25;
                var accepted = comparable && leftBest[leftIndex] < leftSecond[leftIndex] * settings.MatchRatioThreshold
                    && leftBest[leftIndex] <= settings.MatchDistanceThreshold;
                if (accepted)
                {
                    var first = left.Features[leftIndex];
                    var second = right.Features[rightIndex];
                    result.Add(new Match { A = [first.X, first.Y], B = [second.X, second.Y], Score = leftBest[leftIndex], Kind = first.Kind });
                }
            }
            return result;
        }
        finally
        {
            ArrayPool<double>.Shared.Return(distances);
            ArrayPool<int>.Shared.Return(indices);
        }
    }

    private double Distance(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length == 0 || left.Length != right.Length)
        {
            return double.PositiveInfinity;
        }
        double distance = 0;
        for (var index = 0; index < left.Length; index++)
        {
            var difference = left[index] - right[index];
            distance += difference * difference;
        }
        return distance;
    }

    private MatchPixelKey Key(Match match)
    {
        return new MatchPixelKey((long)Math.Round(match.A[0] * 10), (long)Math.Round(match.A[1] * 10),
            (long)Math.Round(match.B[0] * 10), (long)Math.Round(match.B[1] * 10));
    }
}
