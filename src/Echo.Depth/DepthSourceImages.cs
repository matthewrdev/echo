using System.Buffers;
using Echo.Models;
using Echo.Imaging;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Depth;

internal sealed class DepthSourceImages : IDisposable
{
    private readonly IRasterImageService images;
    private readonly IProjectStore store;
    private readonly Dictionary<string, SKBitmap> sources = new(StringComparer.Ordinal);
    private readonly List<float[]> levelBuffers;
    private readonly DepthDecodedImageCache? decodedImages;

    public DepthSourceImages(IRasterImageService images, IProjectStore store, int imageCount, DepthDecodedImageCache? decodedImages = null)
    {
        this.images = images;
        this.store = store;
        this.decodedImages = decodedImages;
        levelBuffers = new List<float[]>(imageCount);
    }

    public GrayscaleImage ReadLevel(Photo photo, int width, bool filter = false, CancellationToken cancellation = default)
    {
        if (!sources.TryGetValue(photo.Source, out var source))
        {
            var path = store.PathFor(photo.Source);
            source = decodedImages?.Rent(path, () => images.Decode(path)) ?? images.Decode(path);
            sources.Add(photo.Source, source);
        }

        // Resize through a borrowed native pixel span. Retain the bitmap until all
        // pyramid levels finish instead of allocating a full-resolution colour copy.
        var size = images.ResizeDimensions(source.Width, source.Height, width);
        var pixels = ArrayPool<float>.Shared.Rent(checked(size.Width * size.Height));
        levelBuffers.Add(pixels);
        var gray = images.ResizeGray(source, width, pixels);
        if (filter)
        {
            StereoImageFilter.Apply(pixels.AsSpan(0, gray.Width * gray.Height), gray.Width, gray.Height, cancellation);
        }

        return gray;
    }

    public void ReleaseLevel()
    {
        foreach (var buffer in levelBuffers)
        {
            ArrayPool<float>.Shared.Return(buffer);
        }

        levelBuffers.Clear();
    }

    public void Dispose()
    {
        ReleaseLevel();
        foreach (var source in sources)
        {
            if (decodedImages == null)
            {
                source.Value.Dispose();
            }
            else
            {
                decodedImages.Return(store.PathFor(source.Key));
            }
        }

        sources.Clear();
    }
}
