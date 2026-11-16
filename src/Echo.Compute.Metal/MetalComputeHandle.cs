using Microsoft.Win32.SafeHandles;

namespace Echo.Compute.Metal;

internal sealed class MetalComputeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public MetalComputeHandle(IntPtr handle) : base(true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        MetalComputeNative.Destroy(handle);
        return true;
    }
}
