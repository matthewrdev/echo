using Echo.Contracts;
namespace Echo.Matching;

public interface IDescriptorMatcher
{
    DescriptorMatches Match<T>(ReadOnlySpan<T> a, ReadOnlySpan<T> b, Settings settings, CancellationToken cancellation, bool reversible = false) where T : IFeatureDescriptor;
}
