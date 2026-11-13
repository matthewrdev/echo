using Echo.Contracts;

namespace Echo.Matching;

public interface IFeatureMatcher
{
    ImagePair Match(FeatureSet leftFeatures, FeatureSet rightFeatures, Settings settings, CancellationToken cancellation);
}
