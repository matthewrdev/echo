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

    public ImagePair Match(FeatureSet leftFeatures, FeatureSet rightFeatures, Settings settings, CancellationToken cancellation)
    {
        var points = descriptors.Match<Feature>(CollectionsMarshal.AsSpan(leftFeatures.Features),
            CollectionsMarshal.AsSpan(rightFeatures.Features), settings, cancellation);
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

        var lines = descriptors.Match<LineFeature>(CollectionsMarshal.AsSpan(leftFeatures.Lines),
            CollectionsMarshal.AsSpan(rightFeatures.Lines), settings, cancellation, allowReversedDescriptor: true);
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
