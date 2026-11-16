namespace Echo.Compute;

// Slots own their storage until disposal. Dispatches run in order; reading or
// overwriting a slot waits for submitted work before touching its shared memory.
public interface IComputeSession : IDisposable
{
    string Device
    {
        get;
    }
    void Upload<T>(int slot, ReadOnlySpan<T> values) where T : unmanaged;
    void UploadRange<T>(int slot, int elementOffset, ReadOnlySpan<T> values) where T : unmanaged;
    void Allocate(int slot, int byteCount);
    void Dispatch(string kernel, int count, ReadOnlySpan<int> parameters);
    void Read<T>(int slot, Span<T> destination) where T : unmanaged;
}
