using System.ComponentModel.Composition;
using Echo.Contracts;

namespace Echo.Matching;

[Export(typeof(IDescriptorMatcher)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class DescriptorMatcher : IDescriptorMatcher
{

    [ImportingConstructor]
    public DescriptorMatcher()
    {
    }

    public DescriptorMatches Match(IReadOnlyList<float[]> a, IReadOnlyList<float[]> b, Settings settings, CancellationToken cancellation, bool reversible = false)
    {
        var diagnostics = new MatchingDiagnostics
        {
            FeaturesA = a.Count,
            FeaturesB = b.Count,
            RatioThreshold = settings.MatchRatioThreshold,
            DistanceThreshold = settings.MatchDistanceThreshold
        };
        var result = new DescriptorMatches([], diagnostics);
        if (a.Count < 2 || b.Count < 2)
        {
            diagnostics.RejectedAmbiguous = a.Count;
            return result;
        }

        var reverseBest = Enumerable.Repeat(double.PositiveInfinity, b.Count).ToArray();
        var reverse = new int[b.Count];
        var best = new int[a.Count];
        var scores = new double[a.Count];
        var seconds = new double[a.Count];
        for (var i = 0; i < a.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var descriptorA = a[i];
            var first = double.PositiveInfinity;
            var second = first;
            for (var j = 0; j < b.Count; j++)
            {
                var descriptorB = b[j];
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

        for (var i = 0; i < a.Count; i++)
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
    private double Distance(ReadOnlySpan<float> a, ReadOnlySpan<float> b, bool reverse)
    {
        if (a.Length == 0 || a.Length != b.Length)
        {
            return double.PositiveInfinity;
        }
        double distance = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var difference = a[i] - b[reverse ? b.Length - 1 - i : i];
            distance += difference * difference;
        }
        return distance;
    }

}
