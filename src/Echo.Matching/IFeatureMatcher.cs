using Echo.Models;

namespace Echo.Matching;

public interface IFeatureMatcher
{
    ImagePair Match(FeatureSet leftFeatures, FeatureSet rightFeatures, Settings settings, CancellationToken cancellation);
}
