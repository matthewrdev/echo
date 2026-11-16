using Echo.Contracts;

namespace Echo.Compute;

public sealed record ComputeExecution
{
    public ComputeOperation Operation
    {
        get; init;
    }
    public ComputeBackend Backend
    {
        get; init;
    }
    public ComputePrecision Precision
    {
        get; init;
    }
    public string Device { get; init; } = "";
    public string Implementation { get; init; } = "";
}
