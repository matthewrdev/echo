using Microsoft.Win32.SafeHandles;

namespace Echo.Texturing.Metal;

internal sealed class MetalTextureHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public MetalTextureHandle(IntPtr value) : base(true)
    {
        SetHandle(value);
    }

    protected override bool ReleaseHandle()
    {
        MetalTextureNative.Destroy(handle);
        return true;
    }
}
