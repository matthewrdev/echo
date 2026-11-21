using Echo.Models;
using Echo.Imaging;
using Echo.Infrastructure;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Benchmarks;

public sealed class FeatureOverlay
{
    private readonly IProjectStoreFactory stores;
    private readonly IJsonCodec json;
    private readonly IRasterImageService images;

    public FeatureOverlay(IProjectStoreFactory stores, IJsonCodec json, IRasterImageService images)
    {
        this.stores = stores;
        this.json = json;
        this.images = images;
    }

    public int Run(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: Echo.Benchmarks feature-overlay PROJECT FEATURES.json OUTPUT_DIRECTORY");
            return 2;
        }

        var store = stores.Open(args[0]);
        var project = store.Load();
        var results = json.Read<FeatureSet[]>(args[1]);
        Directory.CreateDirectory(args[2]);
        foreach (var result in results)
        {
            var photo = project.Images.Find(photo => photo.Id == result.ImageId)
                ?? throw new EchoException("Unknown feature image.");
            using var source = SKBitmap.Decode(store.PathFor(photo.Source));
            using var bitmap = images.Resize(source, 1400);
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint { Color = SKColors.Cyan, IsAntialias = true, StrokeWidth = 1.5f };
            var scale = (double)bitmap.Width / photo.Width;
            foreach (var line in result.Lines)
            {
                canvas.DrawLine((float)(line.Start[0] * scale), (float)(line.Start[1] * scale),
                    (float)(line.End[0] * scale), (float)(line.End[1] * scale), paint);
            }

            using var font = new SKFont(SKTypeface.Default, 22);
            paint.Color = new SKColor(0, 0, 0, 200);
            canvas.DrawRect(0, 0, bitmap.Width, 38, paint);
            paint.Color = SKColors.White;
            canvas.DrawText($"{photo.Name} · {result.Lines.Count} line candidates", 12, 27, font, paint);
            images.Save(bitmap, Path.Combine(args[2], result.ImageId + ".png"), SKEncodedImageFormat.Png);
        }

        return 0;
    }
}
