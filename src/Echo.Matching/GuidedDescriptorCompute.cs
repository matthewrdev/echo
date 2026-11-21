using System.Buffers;
using System.Runtime.InteropServices;
using Echo.Compute;
using Echo.Models;

namespace Echo.Matching;

internal static class GuidedDescriptorCompute
{
    public static List<Match>? TrySearch(IComputeService? compute, FeatureSet left, FeatureSet right,
        ReadOnlySpan<double> matrix, Settings settings, double threshold, CancellationToken cancellation)
    {
        var useCpu = settings.ComputeBackend == ComputeBackend.Cpu || (long)left.Features.Count * right.Features.Count < 65536;
        if (compute == null || useCpu)
        {
            return null;
        }

        var lines = ArrayPool<float>.Shared.Rent(left.Features.Count * 4);
        var pixels = ArrayPool<float>.Shared.Rent(right.Features.Count * 2);
        try
        {
            for (var i = 0; i < left.Features.Count; i++)
            {
                var feature = left.Features[i];
                var x = matrix[0] * feature.X + matrix[1] * feature.Y + matrix[2];
                var y = matrix[3] * feature.X + matrix[4] * feature.Y + matrix[5];
                lines[i * 4] = (float)x;
                lines[i * 4 + 1] = (float)y;
                lines[i * 4 + 2] = (float)(matrix[6] * feature.X + matrix[7] * feature.Y + matrix[8]);
                lines[i * 4 + 3] = (float)(threshold * Math.Sqrt(x * x + y * y));
            }

            for (var i = 0; i < right.Features.Count; i++)
            {
                pixels[i * 2] = (float)right.Features[i].X;
                pixels[i * 2 + 1] = (float)right.Features[i].Y;
            }

            var matches = AcceleratedDescriptorMatcher.TryMatch<Feature>(CollectionsMarshal.AsSpan(left.Features),
                CollectionsMarshal.AsSpan(right.Features), settings, false, compute, cancellation,
                lines.AsSpan(0, left.Features.Count * 4), pixels.AsSpan(0, right.Features.Count * 2));
            if (matches == null)
            {
                return null;
            }

            var result = new List<Match>(matches.Matches.Count);
            foreach (var match in matches.Matches)
            {
                var first = left.Features[match.A];
                var second = right.Features[match.B];
                result.Add(new Match { A = [first.X, first.Y], B = [second.X, second.Y], Score = match.Distance, Kind = first.Kind });
            }

            return result;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(lines);
            ArrayPool<float>.Shared.Return(pixels);
        }
    }
}
