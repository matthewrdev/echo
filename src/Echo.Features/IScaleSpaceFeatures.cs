using Echo.Contracts;

namespace Echo.Features;

public interface IScaleSpaceFeatures
{
    List<Feature> Detect(ReadOnlySpan<float> pixels, int width, int height, int limit, CancellationToken cancellation);
}
