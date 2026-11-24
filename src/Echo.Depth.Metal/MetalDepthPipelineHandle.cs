using Microsoft.Win32.SafeHandles;

namespace Echo.Depth.Metal;

internal sealed class MetalDepthPipelineHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public MetalDepthPipelineHandle(IntPtr value, string fingerprint) : base(true)
    {
        SetHandle(value);
        Fingerprint = fingerprint;
    }

    public string Fingerprint
    {
        get;
    }

    protected override bool ReleaseHandle()
    {
        MetalDepthNative.DestroyPipeline(handle);
        return true;
    }
}
