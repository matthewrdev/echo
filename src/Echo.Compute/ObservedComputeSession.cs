namespace Echo.Compute;

internal sealed class ObservedComputeSession : IComputeSession
{
    private readonly IComputeSession session;
    private readonly Action record;

    public ObservedComputeSession(IComputeSession session, Action record)
    {
        this.session = session;
        this.record = record;
    }

    public string Device => session.Device;
    public void Upload<T>(int slot, ReadOnlySpan<T> values) where T : unmanaged => session.Upload(slot, values);
    public void UploadRange<T>(int slot, int elementOffset, ReadOnlySpan<T> values) where T : unmanaged => session.UploadRange(slot, elementOffset, values);
    public void Allocate(int slot, int byteCount) => session.Allocate(slot, byteCount);
    public void Read<T>(int slot, Span<T> destination) where T : unmanaged => session.Read(slot, destination);

    public void Dispatch(string kernel, int count, ReadOnlySpan<int> parameters)
    {
        session.Dispatch(kernel, count, parameters);
        record();
    }

    public void Dispose() => session.Dispose();
}
