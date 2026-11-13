using System.ComponentModel.Composition;
using System.Buffers;
using Echo.Contracts;

namespace Echo.Matching;

[Export(typeof(IDescriptorMatcher)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class DescriptorMatcher : IDescriptorMatcher
{

    [ImportingConstructor]
    public DescriptorMatcher()
    {
    }

    public DescriptorMatches Match<T>(ReadOnlySpan<T> a, ReadOnlySpan<T> b, Settings settings, CancellationToken cancellation, bool reversible = false) where T : IFeatureDescriptor
    {
        var diagnostics = new MatchingDiagnostics
        {
            FeaturesA = a.Length,
            FeaturesB = b.Length,
            RatioThreshold = settings.MatchRatioThreshold,
            DistanceThreshold = settings.MatchDistanceThreshold
        };
        var result = new DescriptorMatches([], diagnostics);
        if (a.Length < 2 || b.Length < 2)
        {
            diagnostics.RejectedAmbiguous = a.Length;
            return result;
        }

        var numbers = ArrayPool<double>.Shared.Rent(b.Length + a.Length * 2);
        var indices = ArrayPool<int>.Shared.Rent(b.Length + a.Length);
        try
        {
            var reverseBest = numbers.AsSpan(0, b.Length);
            reverseBest.Fill(double.PositiveInfinity);
            var scores = numbers.AsSpan(b.Length, a.Length);
            var seconds = numbers.AsSpan(b.Length + a.Length, a.Length);
            var reverse = indices.AsSpan(0, b.Length);
            var best = indices.AsSpan(b.Length, a.Length);
            reverse.Clear();
            best.Clear();
            for (var i = 0; i < a.Length; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var descriptorA = a[i].Descriptor;
                var first = double.PositiveInfinity;
                var second = first;
                for (var j = 0; j < b.Length; j++)
                {
                    var descriptorB = b[j].Descriptor;
                    var distance = Distance(descriptorA, descriptorB, false);
                    if (reversible)
                    {
                        distance = Math.Min(distance, Distance(descriptorA, descriptorB, true));
                    }

                    if (distance < reverseBest[j])
                    {
                        reverseBest[j] = distance;
                        reverse[j] = i;
                    }

                    if (distance < first)
                    {
                        second = first;
                        first = distance;
                        best[i] = j;
                    }
                    else if (distance < second)
                    {
                        second = distance;
                    }
                }

                scores[i] = first;
                seconds[i] = second;
            }

            for (var i = 0; i < a.Length; i++)
            {
                if (reverse[best[i]] != i)
                {
                    diagnostics.RejectedNonMutual++;
                    continue;
                }

                if (scores[i] >= seconds[i] * settings.MatchRatioThreshold)
                {
                    diagnostics.RejectedAmbiguous++;
                    continue;
                }

                if (scores[i] > settings.MatchDistanceThreshold)
                {
                    diagnostics.RejectedDistance++;
                    continue;
                }

                diagnostics.Accepted++;

                result.Matches.Add(new DescriptorCorrespondence(i, best[i], scores[i]));
            }

            return result;
        }
        finally
        {
            ArrayPool<double>.Shared.Return(numbers);
            ArrayPool<int>.Shared.Return(indices);
        }
    }
    private double Distance(ReadOnlySpan<float> a, ReadOnlySpan<float> b, bool reverse)
    {
        if (a.Length == 0 || a.Length != b.Length)
        {
            return double.PositiveInfinity;
        }
        double distance = 0;
        if (reverse)
        {
            for (var i = 0; i < a.Length; i++)
            {
                var difference = a[i] - b[b.Length - 1 - i];
                distance += difference * difference;
            }
        }
        else
        {
            for (var i = 0; i < a.Length; i++)
            {
                var difference = a[i] - b[i];
                distance += difference * difference;
            }
        }
        return distance;
    }

}
