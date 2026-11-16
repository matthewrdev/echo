using Echo.Contracts;

namespace Echo.Cli;

internal sealed class ProjectOpenResult
{
    public required string Project
    {
        get; init;
    }
    public required ProjectData Data
    {
        get; init;
    }
}
