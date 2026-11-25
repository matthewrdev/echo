using Echo.Projects;
using Echo.Models;

namespace Echo.Pipeline.Artifacts;

public interface ICheckpointStore
{
    string Build(IProjectStore store,
        string stage,
        object inputs,
        Action<string> action,
        CancellationToken cancellation,
        Action<string, Dictionary<string, object?>> progress,
        out bool recordedProcessing,
        Func<ProcessingMetadata>? processing = null,
        string? algorithmVersion = null,
        IReadOnlyList<object>? compatibleInputs = null);
}
