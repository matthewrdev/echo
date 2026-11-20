using System.Runtime.InteropServices;

namespace Echo.Imaging;

internal static class MacImageIo
{
    internal static MacImageHandle CreateOrientedImageOptions()
    {
        var plist = """
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0"><dict>
            <key>kCGImageSourceCreateThumbnailFromImageAlways</key><true/>
            <key>kCGImageSourceCreateThumbnailWithTransform</key><true/>
            </dict></plist>
            """u8.ToArray();
        using var data = CFDataCreate(0, plist, plist.Length);
        return CFPropertyListCreateWithData(0, data, 0, 0, 0);
    }

    internal static MacImageHandle CreateSrgbColourSpace()
    {
        const uint utf8Encoding = 0x08000100;
        using var name = CFStringCreateWithCString(0, "kCGColorSpaceSRGB", utf8Encoding);
        return CGColorSpaceCreateWithName(name);
    }

    internal static bool HasDecodedPixels(MacImageHandle image)
    {
        // Image I/O can return a lazy image even when its HEVC decoder cannot produce pixels.
        var provider = CGImageGetDataProvider(image);
        if (provider == 0)
        {
            return false;
        }

        using var data = CGDataProviderCopyData(provider);
        return !data.IsInvalid && CFDataGetLength(data) > 0;
    }

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    internal static extern MacImageHandle CFURLCreateFromFileSystemRepresentation(nint allocator, byte[] bytes,
        nint length, byte isDirectory);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern MacImageHandle CFDataCreate(nint allocator, byte[] bytes, nint length);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nint CFDataGetLength(MacImageHandle data);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern MacImageHandle CFPropertyListCreateWithData(nint allocator, MacImageHandle data,
        nuint options, nint format, nint error);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern MacImageHandle CFStringCreateWithCString(nint allocator,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value, uint encoding);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    internal static extern void CFRelease(nint value);

    [DllImport("/System/Library/Frameworks/ImageIO.framework/ImageIO")]
    internal static extern MacImageHandle CGImageSourceCreateWithURL(MacImageHandle url, nint options);

    [DllImport("/System/Library/Frameworks/ImageIO.framework/ImageIO")]
    internal static extern nuint CGImageSourceGetPrimaryImageIndex(MacImageHandle source);

    [DllImport("/System/Library/Frameworks/ImageIO.framework/ImageIO")]
    internal static extern MacImageHandle CGImageSourceCreateThumbnailAtIndex(MacImageHandle source, nuint index,
        MacImageHandle options);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern MacImageHandle CGColorSpaceCreateWithName(MacImageHandle name);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    internal static extern nuint CGImageGetWidth(MacImageHandle image);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    internal static extern nuint CGImageGetHeight(MacImageHandle image);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern nint CGImageGetDataProvider(MacImageHandle image);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern MacImageHandle CGDataProviderCopyData(nint provider);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    internal static extern MacImageHandle CGBitmapContextCreate(nint pixels, nuint width, nuint height,
        nuint bitsPerComponent, nuint bytesPerRow, MacImageHandle colourSpace, uint bitmapInfo);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    internal static extern void CGContextDrawImage(MacImageHandle context, MacImageRect destination, MacImageHandle image);
}
