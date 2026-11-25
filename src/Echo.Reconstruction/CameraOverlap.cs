using Echo.Geometry;
using Echo.Models;

namespace Echo.Reconstruction;

internal sealed class CameraOverlap
{
    public const int SampleCount = 24;
    private readonly V3[] leftRays = new V3[SampleCount];
    private readonly V3[] rightRays = new V3[SampleCount];
    private readonly double leftPixelScale;
    private readonly double rightPixelScale;

    public CameraOverlap(int left, int right, Photo leftPhoto, Photo rightPhoto, IReadOnlyList<Match> matches)
    {
        Left = left;
        Right = right;
        var random = new Random(4855);
        for (var sample = 0; sample < SampleCount; sample++)
        {
            // Spread a deterministic sample over the verified matches, without drawing duplicates.
            var index = random.Next(sample * matches.Count / SampleCount, (sample + 1) * matches.Count / SampleCount);
            leftRays[sample] = CentrePixel(leftPhoto, matches[index].A);
            rightRays[sample] = CentrePixel(rightPhoto, matches[index].B);
        }

        leftPixelScale = leftPhoto.Width / 1000.0;
        rightPixelScale = rightPhoto.Width / 1000.0;

        V3 CentrePixel(Photo photo, double[] pixel) =>
            new(pixel[0] - photo.Width * .5, pixel[1] - photo.Height * .5, 0);
    }

    public int Left
    {
        get;
    }
    public int Right
    {
        get;
    }

    public double Error(CameraOverlapFrame left, CameraOverlapFrame right, int sample)
    {
        var baseline = right.Centre - left.Centre;
        var a = left.Ray(leftRays[sample]);
        var b = right.Ray(rightRays[sample]);
        var rightNormal = baseline.Cross(a);
        var leftNormal = b.Cross(baseline);
        var rightScale = rightPixelScale / right.FocalPixels;
        var leftScale = leftPixelScale / left.FocalPixels;
        var rightGradient = rightNormal.Dot(right.Right) * rightScale;
        var rightVerticalGradient = rightNormal.Dot(right.Down) * rightScale;
        var leftGradient = leftNormal.Dot(left.Right) * leftScale;
        var leftVerticalGradient = leftNormal.Dot(left.Down) * leftScale;
        var gradientSquared = rightGradient * rightGradient + rightVerticalGradient * rightVerticalGradient
            + leftGradient * leftGradient + leftVerticalGradient * leftVerticalGradient;

        // Signed Sampson distance in pixels at 1,000 px image width.
        return gradientSquared > 1e-24 ? b.Dot(rightNormal) / Math.Sqrt(gradientSquared) : 1000;
    }

    public double MedianError(CameraOverlapFrame[] frames)
    {
        Span<double> errors = stackalloc double[SampleCount];
        for (var sample = 0; sample < errors.Length; sample++)
        {
            errors[sample] = Math.Abs(Error(frames[Left], frames[Right], sample));
        }

        errors.Sort();
        return (errors[SampleCount / 2 - 1] + errors[SampleCount / 2]) * .5;
    }
}
