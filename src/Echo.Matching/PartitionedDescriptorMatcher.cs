using System.Runtime.InteropServices;
using Echo.Compute;
using Echo.Contracts;

namespace Echo.Matching;

internal static class PartitionedDescriptorMatcher
{
    public static DescriptorMatches? TryMatch<T>(ReadOnlySpan<T> left, ReadOnlySpan<T> right, Settings settings,
        bool reverse, IComputeService? compute, CancellationToken cancellation) where T : IFeatureDescriptor
    {
        var cannotAccelerate = compute == null || settings.ComputeBackend == ComputeBackend.Cpu
            || left.Length < 2 || right.Length < 2;
        if (cannotAccelerate)
        {
            return null;
        }
        var length = left[0].DescriptorData.Span.Length;
        var mixed = false;
        foreach (var feature in left)
        {
            mixed |= feature.DescriptorData.Span.Length != length;
        }
        foreach (var feature in right)
        {
            mixed |= feature.DescriptorData.Span.Length != length;
        }
        if (!mixed)
        {
            return null;
        }

        var groups = new Dictionary<int, DescriptorGroup<T>>();
        for (var index = 0; index < left.Length; index++)
        {
            var group = Group(groups, left[index].DescriptorData.Span.Length);
            group.Left.Add(left[index]);
            group.LeftIndices.Add(index);
        }
        for (var index = 0; index < right.Length; index++)
        {
            var group = Group(groups, right[index].DescriptorData.Span.Length);
            group.Right.Add(right[index]);
            group.RightIndices.Add(index);
        }
        var diagnostics = new MatchingDiagnostics
        {
            FeaturesA = left.Length,
            FeaturesB = right.Length,
            RatioThreshold = settings.MatchRatioThreshold,
            DistanceThreshold = settings.MatchDistanceThreshold
        };
        var result = new DescriptorMatches([], diagnostics);
        // Unmatched descriptors retain the CPU path's default index and rejection
        // classification, even when the first right-hand descriptor has a match.
        var firstRightBest = FirstRightBest(left, right[0].DescriptorData.Span, reverse);
        foreach (var entry in groups)
        {
            cancellation.ThrowIfCancellationRequested();
            var group = entry.Value;
            if (entry.Key == 0 || group.Right.Count == 0)
            {
                foreach (var index in group.LeftIndices)
                {
                    if (index == firstRightBest)
                    {
                        diagnostics.RejectedAmbiguous++;
                    }
                    else
                    {
                        diagnostics.RejectedNonMutual++;
                    }
                }
                continue;
            }
            var first = CollectionsMarshal.AsSpan(group.Left);
            var second = CollectionsMarshal.AsSpan(group.Right);
            var matches = AcceleratedDescriptorMatcher.TryMatch<T>(first, second, settings, reverse, compute, cancellation)
                ?? DescriptorMatcher.MatchCpu<T>(first, second, settings, cancellation, reverse, requireMultipleFeatures: false);
            diagnostics.Accepted += matches.Diagnostics.Accepted;
            diagnostics.RejectedNonMutual += matches.Diagnostics.RejectedNonMutual;
            diagnostics.RejectedAmbiguous += matches.Diagnostics.RejectedAmbiguous;
            diagnostics.RejectedDistance += matches.Diagnostics.RejectedDistance;
            foreach (var match in matches.Matches)
            {
                result.Matches.Add(new DescriptorCorrespondence(group.LeftIndices[match.A], group.RightIndices[match.B], match.Distance));
            }
        }
        result.Matches.Sort((first, second) => first.A.CompareTo(second.A));
        return result;
    }

    private static DescriptorGroup<T> Group<T>(Dictionary<int, DescriptorGroup<T>> groups, int length) where T : IFeatureDescriptor
    {
        if (!groups.TryGetValue(length, out var group))
        {
            group = new DescriptorGroup<T>();
            groups.Add(length, group);
        }
        return group;
    }

    private static int FirstRightBest<T>(ReadOnlySpan<T> left, ReadOnlySpan<float> right, bool reverse) where T : IFeatureDescriptor
    {
        var best = double.PositiveInfinity;
        var index = 0;
        for (var candidate = 0; candidate < left.Length; candidate++)
        {
            var distance = DescriptorMatcher.Distance(left[candidate].DescriptorData.Span, right, false);
            if (reverse)
            {
                distance = Math.Min(distance, DescriptorMatcher.Distance(left[candidate].DescriptorData.Span, right, true));
            }
            if (distance < best)
            {
                best = distance;
                index = candidate;
            }
        }
        return index;
    }
}
