using System.Buffers;
using Echo.Compute;
using Echo.Contracts;

namespace Echo.Matching;

internal static class AcceleratedDescriptorMatcher
{
    public static DescriptorMatches? TryMatch<T>(ReadOnlySpan<T> left, ReadOnlySpan<T> right, Settings settings,
        bool reverse, IComputeService? compute, CancellationToken cancellation, ReadOnlySpan<float> lines = default, ReadOnlySpan<float> pixels = default) where T : IFeatureDescriptor
    {
        var tooFewDescriptors = left.Length < 2 || right.Length < 2;
        if (compute == null || tooFewDescriptors)
        {
            return null;
        }
        if (settings.ComputeBackend != ComputeBackend.Metal && (long)left.Length * right.Length < 65536)
        {
            return null;
        }
        var length = left[0].Descriptor.Length;
        if (length == 0 || length > 1024)
        {
            return null;
        }
        foreach (var feature in left)
        {
            if (feature.Descriptor.Length != length)
            {
                return null;
            }
        }
        foreach (var feature in right)
        {
            if (feature.Descriptor.Length != length)
            {
                return null;
            }
        }
        if (!compute.CanAllocate((long)(left.Length + right.Length) * length * sizeof(float) + (long)Math.Min(64, left.Length) * right.Length * sizeof(float) + (long)left.Length * 32 + (long)right.Length * 16, settings.ComputeBackend))
        {
            return null;
        }
        using var session = compute.Open(settings.ComputeBackend, cancellation);
        if (session == null)
        {
            return null;
        }
        var packed = ArrayPool<float>.Shared.Rent(checked(Math.Max(left.Length, right.Length) * length));
        var minima = ArrayPool<float>.Shared.Rent(checked(left.Length * 4 + right.Length * 2));
        try
        {
            for (var i = 0; i < left.Length; i++)
            {
                left[i].Descriptor.CopyTo(packed.AsSpan(i * length, length));
            }
            session.Upload<float>(0, packed.AsSpan(0, left.Length * length));
            for (var i = 0; i < right.Length; i++)
            {
                right[i].Descriptor.CopyTo(packed.AsSpan(i * length, length));
            }
            session.Upload<float>(1, packed.AsSpan(0, right.Length * length));
            var guided = !lines.IsEmpty;
            session.Upload<float>(5, guided ? lines : [0, 0, 0, 0]);
            session.Upload<float>(6, guided ? pixels : [0, 0]);
            const int rows = 64;
            session.Allocate(2, checked(Math.Min(rows, left.Length) * right.Length * sizeof(float)));
            session.Allocate(3, checked(left.Length * 4 * sizeof(float)));
            session.Allocate(4, checked(right.Length * 2 * sizeof(float)));
            Span<int> parameters = stackalloc int[] { left.Length, right.Length, length, reverse ? 1 : 0, 0, 0, guided ? 1 : 0 };
            for (var first = 0; first < left.Length; first += rows)
            {
                cancellation.ThrowIfCancellationRequested();
                var count = Math.Min(rows, left.Length - first);
                parameters[4] = first;
                parameters[5] = count;
                session.Dispatch(ComputeKernel.DescriptorPairs, checked(count * right.Length), parameters);
                session.Dispatch(ComputeKernel.DescriptorLeft, count, parameters);
                session.Dispatch(ComputeKernel.DescriptorRight, right.Length, parameters);
            }
            var leftBest = minima.AsSpan(0, left.Length * 4);
            var rightBest = minima.AsSpan(leftBest.Length, right.Length * 2);
            session.Read<float>(3, leftBest);
            session.Read<float>(4, rightBest);
            var diagnostics = new MatchingDiagnostics
            {
                FeaturesA = left.Length,
                FeaturesB = right.Length,
                RatioThreshold = settings.MatchRatioThreshold,
                DistanceThreshold = settings.MatchDistanceThreshold
            };
            var result = new DescriptorMatches([], diagnostics);
            for (var a = 0; a < left.Length; a++)
            {
                if (!float.IsFinite(leftBest[a * 4]))
                {
                    diagnostics.RejectedAmbiguous++;
                    continue;
                }
                var b = (int)leftBest[a * 4 + 1];
                if ((int)rightBest[b * 2 + 1] != a)
                {
                    diagnostics.RejectedNonMutual++;
                    continue;
                }
                // Preserve double-precision scores and threshold decisions for the
                // selected neighbours. Ranking uses compensated float arithmetic.
                var best = Distance(left[a].Descriptor, right[b].Descriptor, reverse);
                var second = float.IsFinite(leftBest[a * 4 + 2])
                    ? Distance(left[a].Descriptor, right[(int)leftBest[a * 4 + 3]].Descriptor, reverse) : double.PositiveInfinity;
                var weakSingleCandidate = guided && !double.IsFinite(second) && best > settings.MatchDistanceThreshold * .25;
                if (weakSingleCandidate)
                {
                    diagnostics.RejectedAmbiguous++;
                    continue;
                }
                if (best >= second * settings.MatchRatioThreshold)
                {
                    diagnostics.RejectedAmbiguous++;
                    continue;
                }
                if (best > settings.MatchDistanceThreshold)
                {
                    diagnostics.RejectedDistance++;
                    continue;
                }
                diagnostics.Accepted++;
                result.Matches.Add(new DescriptorCorrespondence(a, b, best));
            }
            return result;
        }
        catch (ComputeException) when (settings.ComputeBackend == ComputeBackend.Auto)
        {
            return null;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(packed);
            ArrayPool<float>.Shared.Return(minima);
        }
    }

    private static double Distance(ReadOnlySpan<float> left, ReadOnlySpan<float> right, bool reverse)
    {
        double sum = 0;
        double reversed = 0;
        for (var i = 0; i < left.Length; i++)
        {
            var difference = left[i] - right[i];
            sum += difference * difference;
            if (reverse)
            {
                difference = left[i] - right[right.Length - i - 1];
                reversed += difference * difference;
            }
        }
        return reverse ? Math.Min(sum, reversed) : sum;
    }
}
