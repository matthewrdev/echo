using System.Buffers;
using Echo.Contracts;

namespace Echo.Matching;

public static class DescriptorMatcher
{
    public static DescriptorMatches Match<T>(ReadOnlySpan<T> leftFeatures, ReadOnlySpan<T> rightFeatures, Settings settings, CancellationToken cancellation, bool allowReversedDescriptor = false) where T : IFeatureDescriptor
    {
        var diagnostics = new MatchingDiagnostics
        {
            FeaturesA = leftFeatures.Length,
            FeaturesB = rightFeatures.Length,
            RatioThreshold = settings.MatchRatioThreshold,
            DistanceThreshold = settings.MatchDistanceThreshold
        };
        var result = new DescriptorMatches([], diagnostics);
        if (leftFeatures.Length < 2 || rightFeatures.Length < 2)
        {
            diagnostics.RejectedAmbiguous = leftFeatures.Length;
            return result;
        }

        var numbers = ArrayPool<double>.Shared.Rent(rightFeatures.Length + leftFeatures.Length * 2);
        var indices = ArrayPool<int>.Shared.Rent(rightFeatures.Length + leftFeatures.Length);
        try
        {
            var rightBestDistances = numbers.AsSpan(0, rightFeatures.Length);
            rightBestDistances.Fill(double.PositiveInfinity);
            var leftBestDistances = numbers.AsSpan(rightFeatures.Length, leftFeatures.Length);
            var leftSecondBestDistances = numbers.AsSpan(rightFeatures.Length + leftFeatures.Length, leftFeatures.Length);
            var rightToLeftIndices = indices.AsSpan(0, rightFeatures.Length);
            var leftToRightIndices = indices.AsSpan(rightFeatures.Length, leftFeatures.Length);
            rightToLeftIndices.Clear();
            leftToRightIndices.Clear();
            for (var leftIndex = 0; leftIndex < leftFeatures.Length; leftIndex++)
            {
                cancellation.ThrowIfCancellationRequested();
                var leftDescriptor = leftFeatures[leftIndex].Descriptor;
                var bestDistance = double.PositiveInfinity;
                var secondBestDistance = bestDistance;
                for (var rightIndex = 0; rightIndex < rightFeatures.Length; rightIndex++)
                {
                    var rightDescriptor = rightFeatures[rightIndex].Descriptor;
                    var distance = Distance(leftDescriptor, rightDescriptor, false);
                    if (allowReversedDescriptor)
                    {
                        distance = Math.Min(distance, Distance(leftDescriptor, rightDescriptor, true));
                    }

                    if (distance < rightBestDistances[rightIndex])
                    {
                        rightBestDistances[rightIndex] = distance;
                        rightToLeftIndices[rightIndex] = leftIndex;
                    }

                    if (distance < bestDistance)
                    {
                        secondBestDistance = bestDistance;
                        bestDistance = distance;
                        leftToRightIndices[leftIndex] = rightIndex;
                    }
                    else if (distance < secondBestDistance)
                    {
                        secondBestDistance = distance;
                    }
                }

                leftBestDistances[leftIndex] = bestDistance;
                leftSecondBestDistances[leftIndex] = secondBestDistance;
            }

            for (var leftIndex = 0; leftIndex < leftFeatures.Length; leftIndex++)
            {
                if (rightToLeftIndices[leftToRightIndices[leftIndex]] != leftIndex)
                {
                    diagnostics.RejectedNonMutual++;
                    continue;
                }

                if (leftBestDistances[leftIndex] >= leftSecondBestDistances[leftIndex] * settings.MatchRatioThreshold)
                {
                    diagnostics.RejectedAmbiguous++;
                    continue;
                }

                if (leftBestDistances[leftIndex] > settings.MatchDistanceThreshold)
                {
                    diagnostics.RejectedDistance++;
                    continue;
                }

                diagnostics.Accepted++;

                result.Matches.Add(new DescriptorCorrespondence(leftIndex, leftToRightIndices[leftIndex], leftBestDistances[leftIndex]));
            }

            return result;
        }
        finally
        {
            ArrayPool<double>.Shared.Return(numbers);
            ArrayPool<int>.Shared.Return(indices);
        }
    }
    private static double Distance(ReadOnlySpan<float> leftDescriptor, ReadOnlySpan<float> rightDescriptor, bool reverseRightDescriptor)
    {
        if (leftDescriptor.Length == 0 || leftDescriptor.Length != rightDescriptor.Length)
        {
            return double.PositiveInfinity;
        }
        double distance = 0;
        if (reverseRightDescriptor)
        {
            for (var i = 0; i < leftDescriptor.Length; i++)
            {
                var difference = leftDescriptor[i] - rightDescriptor[rightDescriptor.Length - 1 - i];
                distance += difference * difference;
            }
        }
        else
        {
            for (var i = 0; i < leftDescriptor.Length; i++)
            {
                var difference = leftDescriptor[i] - rightDescriptor[i];
                distance += difference * difference;
            }
        }
        return distance;
    }

}
