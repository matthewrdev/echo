using Echo.Contracts;
using Echo.SceneSemantics;

namespace Echo.Pipeline;

internal static class VegetationFeatureFilter
{
    internal static FeatureSet Filter(Photo photo, FeatureSet source, SemanticMap map)
    {
        var expectedSamples = (long)map.Width * map.Height;
        var validMask = map.ImageId == photo.Id && map.Width > 0 && map.Height > 0
            && map.VegetationSamples.Count == expectedSamples;
        if (!validMask || photo.Width <= 0 || photo.Height <= 0)
        {
            throw new InvalidDataException($"Invalid vegetation mask for {photo.Name}.");
        }

        const double minimumConfidence = .75;
        return new FeatureSet
        {
            ImageId = source.ImageId,
            // Line segments are inspection-only. Keep their source indices for the feature viewer.
            Lines = source.Lines,
            Features = source.Features.Where(feature =>
                SemanticProbabilities.ConservativeSample(map, map.VegetationSamples,
                    feature.X / photo.Width, feature.Y / photo.Height) < minimumConfidence).ToList()
        };
    }
}
