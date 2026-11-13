using Echo.Projects;

namespace Echo.Pipeline;

public interface IStageResetService
{
    IReadOnlyList<string> Reset(IProjectStore store, string fromStage);
}
