namespace Echo.Compute;

public interface IComputeDevice
{
    string GetImplementation(ComputeOperation operation);
    IComputeSession? Open(CancellationToken cancellation, long memoryBudgetBytes);
    IComputeSession? OpenForMatching(CancellationToken cancellation, long memoryBudgetBytes, long requiredBytes) => Open(cancellation, memoryBudgetBytes);
}
