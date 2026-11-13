using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Imaging;
using Echo.Lines;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Features;

[Export(typeof(IFeatureExtractor)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class FeatureExtractor : IFeatureExtractor
{
    private readonly IRasterImageService rasterImageService;
    private readonly ILineDetector lines;

    [ImportingConstructor]
    public FeatureExtractor(IRasterImageService rasterImageService, ILineDetector lines)
    {
        this.rasterImageService = rasterImageService;
        this.lines = lines;
    }

    public FeatureSet Extract(IProjectStore store, Photo photo, Settings settings, CancellationToken cancellation)
    {
        using var original = SKBitmap.Decode(store.PathFor(photo.Source));
        using var image = rasterImageService.Resize(original, settings.FeatureWidth);
        var gray = rasterImageService.Gray(image);
        var width = image.Width;
        var height = image.Height;
        var gx = new float[gray.Length];
        var gy = new float[gray.Length];
        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                var i = y * width + x;
                gx[i] = (gray[i + 1] - gray[i - 1]) * .5f;
                gy[i] = (gray[i + width] - gray[i - width]) * .5f;
            }
        }

        var corners = new List<Corner>();
        for (var y = 12; y < height - 12; y += 2)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var x = 12; x < width - 12; x += 2)
            {
                double xx = 0;
                double yy = 0;
                double xy = 0;
                for (var dy = -2; dy <= 2; dy++)
                {
                    for (var dx = -2; dx <= 2; dx++)
                    {
                        var i = (y + dy) * width + x + dx;
                        xx += gx[i] * gx[i];
                        yy += gy[i] * gy[i];
                        xy += gx[i] * gy[i];
                    }
                }

                var score = xx * yy - xy * xy - .04 * (xx + yy) * (xx + yy);
                if (score > .00001)
                {
                    corners.Add(new Corner
                    {
                        X = x,
                        Y = y,
                        Score = score
                    });
                }
            }
        }

        var occupied = new bool[gray.Length];
        var output = new FeatureSet
        {
            ImageId = photo.Id
        };
        if (settings.DetectLines)
        {
            output.Lines = lines.Detect(gray, width, height, settings.LineLimit, settings.LineMinimumLength, cancellation);
            var junctions = lines.Junctions(output.Lines, gray, width, height, Math.Max(1, settings.FeatureLimit / 4), cancellation);
            foreach (var point in junctions)
            {
                var cx = (int)Math.Round(point.X);
                var cy = (int)Math.Round(point.Y);
                for (var dy = -7; dy <= 7; dy++)
                {
                    occupied.AsSpan((cy + dy) * width + cx - 7, 15).Fill(true);
                }
                point.X *= (double)photo.Width / width;
                point.Y *= (double)photo.Height / height;
                output.Features.Add(point);
            }
            foreach (var line in output.Lines)
            {
                line.Start[0] *= (double)photo.Width / width;
                line.End[0] *= (double)photo.Width / width;
                line.Start[1] *= (double)photo.Height / height;
                line.End[1] *= (double)photo.Height / height;
            }
        }

        foreach (var corner in corners.OrderByDescending(c => c.Score))
        {
            if (occupied[corner.Y * width + corner.X])
            {
                continue;
            }

            var descriptor = new float[64];
            var n = 0;
            for (var y = -7; y <= 7; y += 2)
            {
                for (var x = -7; x <= 7; x += 2)
                {
                    descriptor[n++] = gray[(corner.Y + y) * width + corner.X + x];
                }
            }

            var mean = descriptor.Average();
            var norm = Math.Sqrt(descriptor.Sum(v => (v - mean) * (v - mean)));
            if (norm < .05)
            {
                continue;
            }

            for (var i = 0; i < descriptor.Length; i++)
            {
                descriptor[i] = (float)((descriptor[i] - mean) / norm);
            }

            output.Features.Add(new Feature
            {
                X = (double)corner.X * photo.Width / width,
                Y = (double)corner.Y * photo.Height / height,
                Descriptor = descriptor
            });
            for (var dy = -7; dy <= 7; dy++)
            {
                for (var dx = -7; dx <= 7; dx++)
                {
                    occupied[(corner.Y + dy) * width + corner.X + dx] = true;
                }
            }

            if (output.Features.Count >= settings.FeatureLimit)
            {
                break;
            }
        }

        return output;
    }
}
