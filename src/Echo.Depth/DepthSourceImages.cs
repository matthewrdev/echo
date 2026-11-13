using Echo.Contracts;
using Echo.Imaging;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Depth;

internal sealed class DepthSourceImages : IDisposable
{
    private readonly IRasterImageService images;
    private readonly IProjectStore store;
    private readonly Dictionary<string, SKBitmap> sources = new(StringComparer.Ordinal);

    public DepthSourceImages(IRasterImageService images, IProjectStore store)
    {
        this.images = images;
        this.store = store;
    }

    public GrayscaleImage ReadLevel(Photo photo, int width)
    {
        if (!sources.TryGetValue(photo.Id, out var source))
        {
            source = images.Decode(store.PathFor(photo.Source));
            sources.Add(photo.Id, source);
        }

        // Resize through a borrowed native pixel span. Retain the bitmap until all
        // pyramid levels finish instead of allocating a full-resolution colour copy.
        return images.ResizeGray(source, width);
    }

    public void Dispose()
    {
        foreach (var source in sources.Values)
        {
            source.Dispose();
        }
        sources.Clear();
    }
}
