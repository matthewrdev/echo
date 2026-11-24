using Echo.Models;
using Echo.Projects;

namespace Echo.Pipeline;

public interface IProcessor
{
    IReadOnlyList<string> Stages
    {
        get;
    }

    RunData Execute(IProjectStore store,
        string target,
        CancellationToken token,
        string? resumeId = null,
        Action<object>? output = null,
        TextureRunOptions? textureOptions = null,
        SubjectDistanceRunOptions? subjectDistanceOptions = null,
        bool headless = false);
}
