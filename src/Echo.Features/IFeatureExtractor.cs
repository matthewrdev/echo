using Echo.Models;
using Echo.Projects;

namespace Echo.Features;

public interface IFeatureExtractor
{
    FeatureSet Extract(IProjectStore store, Photo photo, Settings settings, CancellationToken cancellation);
}
