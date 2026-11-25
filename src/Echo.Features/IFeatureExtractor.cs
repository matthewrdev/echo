using Echo.Models;
using Echo.Projects;

namespace Echo.Features;

public interface IFeatureExtractor
{
    FeatureSet ExtractImage(string path, Photo photo, Settings settings, CancellationToken cancellation) =>
        throw new NotSupportedException("This feature extractor supports project captures only.");

    FeatureSet Extract(IProjectStore store, Photo photo, Settings settings, CancellationToken cancellation);
}
