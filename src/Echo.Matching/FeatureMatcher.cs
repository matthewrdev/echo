using System.Runtime.InteropServices;
using Echo.Contracts;
using Echo.Compute;
using Echo.Telemetry;

namespace Echo.Matching;

public sealed class FeatureMatcher : IFeatureMatcher
{
    private readonly IComputeService? compute;
    private readonly IPerformanceTrace? trace;

    public FeatureMatcher(IComputeService? compute = null,
        IPerformanceTrace? trace = null)
    {
        this.compute = compute;
        this.trace = trace;
    }

    public ImagePair Match(FeatureSet leftFeatures, FeatureSet rightFeatures, Settings settings, CancellationToken cancellation)
    {
        trace?.Measurement("Point descriptors per input", leftFeatures.Features.Count, "descriptors");
        trace?.Measurement("Point descriptors per input", rightFeatures.Features.Count, "descriptors");
        DescriptorMatches points;
        using (var matching = trace?.Begin("Match point descriptors", PerformanceSpanKind.Operation, PerformanceBackend.Mixed))
        {
            points = DescriptorMatcher.Match<Feature>(CollectionsMarshal.AsSpan(leftFeatures.Features),
                CollectionsMarshal.AsSpan(rightFeatures.Features), settings, cancellation, compute: compute);
            matching?.Measure(points.Matches.Count);
        }
        var result = new ImagePair { A = leftFeatures.ImageId, B = rightFeatures.ImageId, Diagnostics = points.Diagnostics };
        foreach (var match in points.Matches)
        {
            var first = leftFeatures.Features[match.A];
            var second = rightFeatures.Features[match.B];
            result.Matches.Add(new Match
            {
                A = [first.X, first.Y],
                B = [second.X, second.Y],
                Score = match.Distance,
                Kind = first.Kind == "junction" || second.Kind == "junction" ? "junction" : first.Kind
            });
        }

        DescriptorMatches lines;
        using (var matching = trace?.Begin("Match line descriptors", PerformanceSpanKind.Operation, PerformanceBackend.Mixed))
        {
            lines = DescriptorMatcher.Match<LineFeature>(CollectionsMarshal.AsSpan(leftFeatures.Lines),
                CollectionsMarshal.AsSpan(rightFeatures.Lines), settings, cancellation, allowReversedDescriptor: true, compute: compute);
            matching?.Measure(lines.Matches.Count);
        }
        foreach (var match in lines.Matches)
        {
            var first = leftFeatures.Lines[match.A];
            var second = rightFeatures.Lines[match.B];
            result.LineMatches.Add(new LineMatch
            {
                IndexA = match.A,
                IndexB = match.B,
                StartA = first.Start,
                EndA = first.End,
                StartB = second.Start,
                EndB = second.End,
                Score = match.Distance
            });
        }
        return result;
    }
}
