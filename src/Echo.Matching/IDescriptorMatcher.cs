using Echo.Contracts;
namespace Echo.Matching;

public interface IDescriptorMatcher
{
    DescriptorMatches Match(IReadOnlyList<float[]> a, IReadOnlyList<float[]> b, Settings settings, CancellationToken cancellation, bool reversible = false);
}
