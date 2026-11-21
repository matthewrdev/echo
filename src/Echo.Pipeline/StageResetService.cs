using Echo.Contracts;
using Echo.Projects;

namespace Echo.Pipeline;

public sealed class StageResetService : IStageResetService
{
    private readonly IReadOnlyList<string> stages;

    public StageResetService(IEnumerable<IPipelineStage> stages)
    {
        this.stages = stages.OrderBy(stage => stage.Order).Select(stage => stage.Name).ToArray();
    }

    public IReadOnlyList<string> Reset(IProjectStore store, string fromStage)
    {
        if (!stages.Contains(fromStage))
        {
            throw new EchoException("Unknown reset stage: " + fromStage, 2);
        }

        using var lease = store.Lock("run");
        var affected = stages.SkipWhile(stage => stage != fromStage).ToArray();
        var revision = Guid.NewGuid().ToString("N");
        store.Edit(project =>
        {
            foreach (var stage in affected)
            {
                project.StageRevisions[stage] = revision;
            }
        });
        return affected;
    }
}
