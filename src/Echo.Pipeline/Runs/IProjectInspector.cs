using Echo.Models;
using Echo.Projects;

namespace Echo.Pipeline.Runs;

public interface IProjectInspector
{
    Inspection Inspect(IProjectStore store, string runId = "latest", bool summary = false);
}
