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
    // Descriptor inputs are read-only in the session. True means the device reused a resident copy.
    bool UploadDescriptor(int slot, ReadOnlyMemory<float> values)
    {
        Upload(slot, values.Span);
        return false;
    }

    void UploadRange<T>(int slot, int elementOffset, ReadOnlySpan<T> values) where T : unmanaged;
    void Allocate(int slot, int byteCount);
    void Dispatch(ComputeKernel kernel, int count, ReadOnlySpan<int> parameters);
    void Read<T>(int slot, Span<T> destination) where T : unmanaged;
}
