using Echo.Models;
using SkiaSharp;

namespace Echo.Rendering.Previews;

public static class PhotoOverlayPreview
{
    public static void Features(SKCanvas canvas, PhotoPreview photo, FeatureSet features)
    {
        photo.Draw(canvas);
        canvas.Save();
        canvas.ClipRect(photo.Bounds);
        using var paint = new SKPaint { IsAntialias = true, StrokeWidth = 1.6f, Color = new SKColor(86, 226, 255, 215) };
        foreach (var line in features.Lines)
        {
            canvas.DrawLine(photo.Point(line.Start), photo.Point(line.End), paint);
        }

        foreach (var point in features.Features)
        {
            var junction = point.Kind == "junction";
            paint.Color = junction ? new SKColor(255, 152, 72, 238) : new SKColor(241, 255, 137, 220);
            canvas.DrawCircle(photo.Point(point.X, point.Y), junction ? 2.7f : 1.7f, paint);
        }

        canvas.Restore();
    }

    public static int Matching(SKCanvas canvas, PhotoPreview left, PhotoPreview right, ImagePair pair, int limit)
    {
        left.Draw(canvas);
        right.Draw(canvas);
        using var paint = new SKPaint { IsAntialias = true, StrokeWidth = 1.2f };
        var count = pair.Matches.Count + pair.LineMatches.Count;
        var displayed = Math.Min(count, limit);
        for (var sample = 0; sample < displayed; sample++)
        {
            var index = (int)((long)sample * count / displayed);
            if (index < pair.Matches.Count)
            {
                var point = pair.Matches[index];
                paint.Color = point.GeometricInlier switch
                {
                    true => new SKColor(104, 247, 181, 165),
                    false => new SKColor(255, 131, 106, 105),
                    null => new SKColor(235, 237, 155, 155)
                };
                var start = left.Point(point.A);
                var end = right.Point(point.B);
                canvas.DrawLine(start, end, paint);
                canvas.DrawCircle(start, 2, paint);
                canvas.DrawCircle(end, 2, paint);
            }
            else
            {
                var line = pair.LineMatches[index - pair.Matches.Count];
                var startA = left.Point(line.StartA);
                var endA = left.Point(line.EndA);
                var startB = right.Point(line.StartB);
                var endB = right.Point(line.EndB);
                paint.Color = new SKColor(97, 210, 255, 210);
                canvas.DrawLine(startA, endA, paint);
                canvas.DrawLine(startB, endB, paint);
                paint.Color = paint.Color.WithAlpha(145);
                canvas.DrawLine(new SKPoint((startA.X + endA.X) / 2, (startA.Y + endA.Y) / 2),
                    new SKPoint((startB.X + endB.X) / 2, (startB.Y + endB.Y) / 2), paint);
            }
        }

        return displayed;
    }
}
