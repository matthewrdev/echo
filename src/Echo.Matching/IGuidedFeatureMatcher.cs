using Echo.Contracts;

namespace Echo.Matching;

public interface IGuidedFeatureMatcher
{
    ImagePair? Recover(FeatureSet left, FeatureSet right, IReadOnlyList<Match> initialMatches, Settings settings,
        double thresholdPixels, CancellationToken cancellation);
}
