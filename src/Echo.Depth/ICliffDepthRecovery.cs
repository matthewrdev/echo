using Echo.Models;
using Echo.Projects;

namespace Echo.Depth;

public interface ICliffDepthRecovery
{
    CliffRecoveryResult Recover(IProjectStore store, ProjectData project, ReconstructionScene scene,
        DepthMap source, IReadOnlyList<DepthMap> originalMaps, SemanticMap semantics, CancellationToken cancellation,
        Action<string>? progress = null);
}
