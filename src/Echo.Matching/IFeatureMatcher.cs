using Echo.Contracts;

namespace Echo.Matching;

public interface IFeatureMatcher
{
    ImagePair Match(FeatureSet a, FeatureSet b, Settings settings, CancellationToken cancellation);
}
