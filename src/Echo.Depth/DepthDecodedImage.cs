using SkiaSharp;

namespace Echo.Depth;

internal sealed class DepthDecodedImage
{
    public DepthDecodedImage(string path, Func<SKBitmap> decode)
    {
        Path = path;
        Bitmap = new Lazy<SKBitmap>(decode);
        IdleNode = new LinkedListNode<DepthDecodedImage>(this);
    }

    public string Path
    {
        get;
    }
    public Lazy<SKBitmap> Bitmap
    {
        get;
    }
    public LinkedListNode<DepthDecodedImage> IdleNode
    {
        get;
    }
    public int Users
    {
        get; set;
    }
    public long Bytes => Bitmap.IsValueCreated ? (long)Bitmap.Value.RowBytes * Bitmap.Value.Height : 0;
}
