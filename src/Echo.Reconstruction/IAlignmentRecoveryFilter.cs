using Echo.Contracts;

namespace Echo.Reconstruction;

public interface IAlignmentRecoveryFilter
{
    List<ImagePair> Filter(ProjectData project, ReconstructionScene scene, IReadOnlyList<ImagePair> originalPairs,
        IReadOnlyList<ImagePair> proposedPairs, CancellationToken cancellation, Action<int, int>? progress = null);
}
