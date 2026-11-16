using System.Buffers;
using Echo.Contracts;
using Echo.Compute;

namespace Echo.Features;

public static class ScaleSpaceFeatures
{
    public static List<Feature> Detect(ReadOnlySpan<float> pixels, int width, int height, int limit, CancellationToken cancellation, IComputeSession? acceleration = null)
    {
        var invalid = width < 1 || height < 1 || pixels.Length != checked(width * height) || limit < 1;
        if (invalid)
        {
            throw new ArgumentException("Scale-space input dimensions and feature limit must be valid.");
        }
        cancellation.ThrowIfCancellationRequested();
        var candidates = new PriorityQueue<ScaleFeatureCandidate, double>();
        var basis = ArrayPool<float>.Shared.Rent(width * height);
        try
        {
            GaussianBlur.Apply(pixels, basis.AsSpan(0, width * height), width, height, Math.Sqrt(1.6 * 1.6 - .5 * .5), cancellation, acceleration);
            var octaveScale = 1.0;
            while (Math.Min(width, height) >= 32)
            {
                var next = ProcessOctave(basis, width, height, octaveScale, limit, candidates, cancellation, acceleration);
                ArrayPool<float>.Shared.Return(basis);
                basis = next;
                width /= 2;
                height /= 2;
                octaveScale *= 2;
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(basis);
        }
        var ordered = new List<ScaleFeatureCandidate>(candidates.Count);
        while (candidates.TryDequeue(out var candidate, out _))
        {
            ordered.Add(candidate);
        }
        ordered.Sort(Compare);
        var result = new List<Feature>(ordered.Count);
        foreach (var candidate in ordered)
        {
            result.Add(candidate.Feature);
        }
        return result;
    }

    private static int Compare(ScaleFeatureCandidate left, ScaleFeatureCandidate right)
    {
        var response = right.Response.CompareTo(left.Response);
        return response != 0 ? response : left.Order.CompareTo(right.Order);
    }

    private static float[] ProcessOctave(float[] basis, int width, int height, double octaveScale, int limit,
        PriorityQueue<ScaleFeatureCandidate, double> candidates, CancellationToken cancellation, IComputeSession? acceleration)
    {
        var length = width * height;
        var gaussian = new float[6][];
        var differences = new float[5][];
        gaussian[0] = basis;
        try
        {
            for (var layer = 1; layer < gaussian.Length; layer++)
            {
                var previousSigma = 1.6 * Math.Pow(2, (layer - 1) / 3.0);
                var nextSigma = 1.6 * Math.Pow(2, layer / 3.0);
                gaussian[layer] = ArrayPool<float>.Shared.Rent(length);
                GaussianBlur.Apply(gaussian[layer - 1].AsSpan(0, length), gaussian[layer].AsSpan(0, length), width, height,
                    Math.Sqrt(nextSigma * nextSigma - previousSigma * previousSigma), cancellation, acceleration);
                differences[layer - 1] = ArrayPool<float>.Shared.Rent(length);
                for (var pixel = 0; pixel < length; pixel++)
                {
                    differences[layer - 1][pixel] = gaussian[layer][pixel] - gaussian[layer - 1][pixel];
                }
            }
            var keypoints = ScaleSpaceExtrema.Find(differences, width, height, cancellation);
            Span<double> angles = stackalloc double[36];
            Span<float> descriptor = stackalloc float[128];
            var order = (int)Math.Log2(octaveScale) * limit * 36;
            foreach (var point in keypoints)
            {
                cancellation.ThrowIfCancellationRequested();
                if (candidates.Count >= limit && candidates.TryPeek(out _, out var minimum) && point.Response <= minimum)
                {
                    break;
                }
                var pixels = gaussian[point.Layer].AsSpan(0, length);
                var count = GradientDescriptor.Orientations(pixels, width, height, point, angles);
                for (var orientation = 0; orientation < count; orientation++)
                {
                    if (!GradientDescriptor.Describe(pixels, width, height, point, angles[orientation], descriptor))
                    {
                        continue;
                    }
                    var feature = new Feature
                    {
                        Kind = "scale-space",
                        X = point.X * octaveScale,
                        Y = point.Y * octaveScale,
                        Descriptor = descriptor.ToArray()
                    };
                    candidates.Enqueue(new ScaleFeatureCandidate(feature, point.Response, order++), point.Response);
                    if (candidates.Count > limit)
                    {
                        candidates.Dequeue();
                    }
                }
            }
            var nextWidth = width / 2;
            var nextHeight = height / 2;
            var next = ArrayPool<float>.Shared.Rent(nextWidth * nextHeight);
            for (var y = 0; y < nextHeight; y++)
            {
                for (var x = 0; x < nextWidth; x++)
                {
                    next[y * nextWidth + x] = gaussian[3][2 * y * width + 2 * x];
                }
            }
            return next;
        }
        finally
        {
            for (var layer = 1; layer < gaussian.Length; layer++)
            {
                if (gaussian[layer] != null)
                {
                    ArrayPool<float>.Shared.Return(gaussian[layer]);
                }
                if (differences[layer - 1] != null)
                {
                    ArrayPool<float>.Shared.Return(differences[layer - 1]);
                }
            }
        }
    }
}
