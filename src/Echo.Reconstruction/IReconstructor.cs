using Echo.Models;

namespace Echo.Reconstruction;

public interface IReconstructor
{
    ReconstructionScene Align(ProjectData project,
        List<ImagePair> pairs,
        CancellationToken cancellation,
        Action<ReconstructionScene>? checkpoint = null,
        Action<OperationProgress>? progress = null);

    ReconstructionScene? TryReconcileOverlaps(ProjectData project, ReconstructionScene established,
        IReadOnlyList<ImagePair> pairs, CancellationToken cancellation, Action<OperationProgress>? progress = null);
}
