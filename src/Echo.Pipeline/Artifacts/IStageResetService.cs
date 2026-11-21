using Echo.Projects;

namespace Echo.Pipeline.Artifacts;

public interface IStageResetService
{
    IReadOnlyList<string> Reset(IProjectStore store, string fromStage);
}
