using Echo.Contracts;
using Echo.Projects;

namespace Echo.Pipeline;

public interface IRunContextFactory
{
    RunContext Create(IProjectStore store, RunData run, CancellationToken cancellation, Action<object>? output);
}
