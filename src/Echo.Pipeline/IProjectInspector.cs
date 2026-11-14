using Echo.Contracts;
using Echo.Projects;

namespace Echo.Pipeline;

public interface IProjectInspector
{
    Inspection Inspect(IProjectStore store, string runId = "latest", bool summary = false);
}
