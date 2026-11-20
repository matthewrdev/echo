using System.Runtime.InteropServices;

namespace Echo.Imaging;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct MacImageRect
{
    private readonly double x;
    private readonly double y;
    private readonly double width;
    private readonly double height;

    internal MacImageRect(double x, double y, double width, double height)
    {
        this.x = x;
        this.y = y;
        this.width = width;
        this.height = height;
    }
}
