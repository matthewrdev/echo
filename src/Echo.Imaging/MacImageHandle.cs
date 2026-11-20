using Microsoft.Win32.SafeHandles;

namespace Echo.Imaging;

internal sealed class MacImageHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public MacImageHandle() : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle()
    {
        MacImageIo.CFRelease(handle);
        return true;
    }
}
