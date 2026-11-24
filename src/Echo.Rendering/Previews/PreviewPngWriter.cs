using Echo.Models;
using SkiaSharp;

namespace Echo.Rendering.Previews;

public sealed class PreviewPngWriter : IPreviewPngWriter
{
    public T Write<T>(string output, int width, int height, Func<SKCanvas, T> draw)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(PreviewImage.Background());
        var result = draw(canvas);
        canvas.Flush();

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (!bitmap.Encode(stream, SKEncodedImageFormat.Png, 100))
                {
                    throw new EchoException("Could not encode the preview PNG.");
                }
            }

            File.Move(temporary, output, overwrite: false);
            return result;
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
