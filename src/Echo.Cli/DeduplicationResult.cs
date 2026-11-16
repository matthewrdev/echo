using Echo.Projects;

namespace Echo.Cli;

internal sealed class DeduplicationResult
{
    public required bool Applied
    {
        get; init;
    }
    public required PhotoDeduplicationPlan Plan
    {
        get; init;
    }
}
