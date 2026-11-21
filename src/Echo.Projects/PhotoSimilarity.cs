using System.Globalization;
using Echo.Models;
using Echo.Imaging;

namespace Echo.Projects;

public static class PhotoSimilarity
{
    public static bool Matches(PhotoSimilaritySample first, PhotoSimilaritySample second)
    {
        var a = first.Photo;
        var b = second.Photo;
        var sameFormat = a.Camera == b.Camera && a.Width == b.Width && a.Height == b.Height;
        if (!sameFormat || Math.Abs(a.FocalPixels - b.FocalPixels) > Math.Max(1, a.FocalPixels * .005))
        {
            return false;
        }

        var differentCapture = TryCaptureTime(a.CapturedAt, out var firstTime) && TryCaptureTime(b.CapturedAt, out var secondTime)
            && Math.Abs((firstTime - secondTime).TotalSeconds) > 10;
        if (differentCapture)
        {
            return false;
        }

        var hasPositions = a.Gps != null && b.Gps != null;
        if (hasPositions && Distance(a.Gps!, b.Gps!) > .75)
        {
            return false;
        }

        if (a.Attitude != null && b.Attitude != null)
        {
            var sameDirection = Angle(a.Attitude.Yaw, b.Attitude.Yaw) <= 1
                && Angle(a.Attitude.Pitch, b.Attitude.Pitch) <= 1
                && Angle(a.Attitude.Roll, b.Attitude.Roll) <= 1;
            if (!sameDirection)
            {
                return false;
            }
        }

        var sameCaptureView = hasPositions && a.Attitude != null && b.Attitude != null
            && CloseInTime(a.CapturedAt, b.CapturedAt);
        var left = sameCaptureView ? first.Overview : first.Image;
        var right = sameCaptureView ? second.Overview : second.Image;
        if (left.Width != right.Width || left.Height != right.Height)
        {
            return false;
        }
        // Position, frustum and capture interval drive drone burst selection.
        // A coarse, unwarped comparison tolerates capture jitter but still rejects
        // changed content. Missing pose/time data requires near-identical pixels.
        var correlation = sameCaptureView ? .95 : .998;
        var error = sameCaptureView ? .07 : .01;
        if (!SimilarRegion(left, right, 0, 0, left.Width, left.Height, correlation, error))
        {
            return false;
        }

        if (sameCaptureView)
        {
            return true;
        }

        var matchingTiles = 0;
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                if (SimilarRegion(left, right, x * left.Width / 4, y * left.Height / 4,
                    (x + 1) * left.Width / 4, (y + 1) * left.Height / 4, correlation - .02, error * 1.5))
                {
                    matchingTiles++;
                }
            }
        }

        return matchingTiles >= 14;
    }

    public static double Sharpness(GrayscaleImage image)
    {
        double energy = 0;
        var pixels = image.Pixels;
        for (var y = 1; y < image.Height - 1; y++)
        {
            for (var x = 1; x < image.Width - 1; x++)
            {
                var index = y * image.Width + x;
                var laplacian = 4 * pixels[index] - pixels[index - 1] - pixels[index + 1]
                    - pixels[index - image.Width] - pixels[index + image.Width];
                energy += laplacian * laplacian;
            }
        }

        return energy / pixels.Length;
    }

    private static bool SimilarRegion(GrayscaleImage a, GrayscaleImage b, int x0, int y0, int x1, int y1,
        double minimumCorrelation, double maximumError)
    {
        double sumA = 0, sumB = 0, squareA = 0, squareB = 0, product = 0;
        var count = (x1 - x0) * (y1 - y0);
        if (count == 0)
        {
            return false;
        }

        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var index = y * a.Width + x;
                var left = a.Pixels[index];
                var right = b.Pixels[index];
                sumA += left;
                sumB += right;
                squareA += left * left;
                squareB += right * right;
                product += left * right;
            }
        }

        var varianceA = Math.Max(0, squareA / count - Math.Pow(sumA / count, 2));
        var varianceB = Math.Max(0, squareB / count - Math.Pow(sumB / count, 2));
        var covariance = product / count - sumA * sumB / count / count;
        var error = Math.Sqrt(Math.Max(0, varianceA + varianceB - 2 * covariance));
        var meanDifference = Math.Abs(sumA - sumB) / count;
        if (meanDifference > .04 || error > maximumError)
        {
            return false;
        }
        // Textureless photos cannot establish that two viewpoints are the same.
        // Flat tiles (e.g. sky) are permitted only inside a textured whole image.
        if (Math.Min(varianceA, varianceB) < .0001)
        {
            var wholeImage = x0 == 0 && y0 == 0 && x1 == a.Width && y1 == a.Height;
            return !wholeImage && Math.Max(varianceA, varianceB) < .0001;
        }

        return covariance / Math.Sqrt(varianceA * varianceB) >= minimumCorrelation;
    }

    private static bool CloseInTime(string a, string b)
    {
        return TryCaptureTime(a, out var first) && TryCaptureTime(b, out var second)
            && Math.Abs((first - second).TotalSeconds) <= 10;
    }

    private static bool TryCaptureTime(string text, out DateTime time)
    {
        return DateTime.TryParseExact(text, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out time);
    }

    private static double Angle(double a, double b) => Math.Abs(Math.IEEERemainder(a - b, 360));

    private static double Distance(GeoPosition a, GeoPosition b)
    {
        var radians = Math.PI / 180;
        var latitude = (a.Latitude + b.Latitude) * .5 * radians;
        var x = Math.IEEERemainder(a.Longitude - b.Longitude, 360) * radians * Math.Cos(latitude) * 6371000;
        var y = (a.Latitude - b.Latitude) * radians * 6371000;
        var z = a.Altitude.HasValue && b.Altitude.HasValue ? a.Altitude.Value - b.Altitude.Value : 0;
        return Math.Sqrt(x * x + y * y + z * z);
    }
}
