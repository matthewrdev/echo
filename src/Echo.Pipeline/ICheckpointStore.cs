using Echo.Projects;

namespace Echo.Pipeline;

public interface ICheckpointStore
{
    string Build(IProjectStore store,
        string stage,
        object inputs,
        Action<string> action,
        CancellationToken cancellation,
Action<string, object> progress);
}
