using Echo.Models;

namespace Echo.Reconstruction;

public interface IReconstructor
{
    ReconstructionScene Align(ProjectData project,
        List<ImagePair> pairs,
        CancellationToken cancellation,
        Action<ReconstructionScene>? checkpoint = null,
        Action<OperationProgress>? progress = null);
}
