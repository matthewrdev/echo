namespace Echo.Compute;

// Explicit bindings let independent upload slots overlap ordered GPU work.
// Submission is bounded; reads, slot reuse and disposal drain relevant work.
public interface IComputeStreamingSession : IComputeSession
{
    bool SupportsStreaming
    {
        get;
    }
    void DispatchBound(ComputeKernel kernel, int count, ReadOnlySpan<int> parameters, ReadOnlySpan<int> slots);
    void Submit();
}
