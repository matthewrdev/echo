using Echo.Models;

namespace Echo.Storage;

public interface IMatchArtifactStore
{
    MatchArtifactHeader Header(string path);
    ImagePair Read(string path);
    void Write(string path, ImagePair pair, CancellationToken cancellation = default);
}
