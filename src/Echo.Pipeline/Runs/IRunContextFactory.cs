using Echo.Models;
using Echo.Projects;

namespace Echo.Pipeline.Runs;

public interface IRunContextFactory
{
    RunContext Create(IProjectStore store, RunData run, CancellationToken cancellation, Action<object>? output, bool headless = false);
}
