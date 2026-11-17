using Echo.Contracts;

namespace Echo.Reconstruction;

public interface IVisualReconstructor
{
    ReconstructionScene? TryAlignVerified(ProjectData project, List<ImagePair> pairs,
        CancellationToken cancellation, Action<ReconstructionScene>? checkpoint = null);
}
