namespace Echo.Compute;

public interface IComputeDevice
{
    string Implementation
    {
        get;
    }
    IComputeSession? Open(CancellationToken cancellation, long memoryBudgetBytes);
}
