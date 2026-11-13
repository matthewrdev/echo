using Echo.Contracts;

namespace Echo.Optimization;

public interface IBundleAdjuster
{
    void Refine(ProjectData project,
        ReconstructionScene scene,
        IReadOnlyList<ImagePair> pairs,
        CancellationToken cancellation,
Action<ReconstructionScene>? checkpoint = null);
}
