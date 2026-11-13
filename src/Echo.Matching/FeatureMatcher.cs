using System.ComponentModel.Composition;
using System.Runtime.InteropServices;
using Echo.Contracts;

namespace Echo.Matching;

[Export(typeof(IFeatureMatcher)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class FeatureMatcher : IFeatureMatcher
{
    private readonly IDescriptorMatcher descriptors;

    [ImportingConstructor]
    public FeatureMatcher(IDescriptorMatcher descriptors)
    {
        this.descriptors = descriptors;
    }

    public ImagePair Match(FeatureSet a, FeatureSet b, Settings settings, CancellationToken cancellation)
    {
        var points = descriptors.Match<Feature>(CollectionsMarshal.AsSpan(a.Features),
            CollectionsMarshal.AsSpan(b.Features), settings, cancellation);
        var result = new ImagePair { A = a.ImageId, B = b.ImageId, Diagnostics = points.Diagnostics };
        foreach (var match in points.Matches)
        {
            var first = a.Features[match.A];
            var second = b.Features[match.B];
            result.Matches.Add(new Match
            {
                A = [first.X, first.Y],
                B = [second.X, second.Y],
                Score = match.Distance,
                Kind = first.Kind == "junction" || second.Kind == "junction" ? "junction" : "corner"
            });
        }

        var lines = descriptors.Match<LineFeature>(CollectionsMarshal.AsSpan(a.Lines),
            CollectionsMarshal.AsSpan(b.Lines), settings, cancellation, reversible: true);
        foreach (var match in lines.Matches)
        {
            var first = a.Lines[match.A];
            var second = b.Lines[match.B];
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
