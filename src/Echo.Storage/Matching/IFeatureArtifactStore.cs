using Echo.Models;

namespace Echo.Storage.Matching;

public interface IFeatureArtifactStore
{
    FeatureArtifactHeader Header(string path);
    FeatureSet Read(string path, bool descriptors = true);
    void Write(string path, FeatureSet features, CancellationToken cancellation = default);
}
