using Microsoft.Win32.SafeHandles;

namespace Echo.Depth.Metal;

internal sealed class MetalDepthHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public MetalDepthHandle(IntPtr value) : base(true)
    {
        SetHandle(value);
    }

    protected override bool ReleaseHandle()
    {
        MetalDepthNative.Destroy(handle);
        return true;
    }
}
