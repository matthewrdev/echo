using Echo.Contracts;
namespace Echo.Matching;

public interface IDescriptorMatcher
{
    /// <summary>
    /// Matches features from the left and right input images. Output A indexes leftFeatures;
    /// B indexes rightFeatures. Reversed descriptors handle line endpoint direction, not image order.
    /// </summary>
    DescriptorMatches Match<T>(ReadOnlySpan<T> leftFeatures, ReadOnlySpan<T> rightFeatures, Settings settings, CancellationToken cancellation, bool allowReversedDescriptor = false) where T : IFeatureDescriptor;
}
