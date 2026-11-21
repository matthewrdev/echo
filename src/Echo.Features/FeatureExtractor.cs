using System.Buffers;
using System.Runtime.ExceptionServices;
using Echo.Models;
using Echo.Compute;
using Echo.Imaging;
using Echo.Lines;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Features;

public sealed class FeatureExtractor : IFeatureExtractor
{
    private readonly IRasterImageService rasterImageService;
    private readonly ILineDetector lines;
    private readonly IComputeService? compute;

    public FeatureExtractor(IRasterImageService rasterImageService, ILineDetector lines, IComputeService? compute = null)
    {
        this.rasterImageService = rasterImageService;
        this.lines = lines;
        this.compute = compute;
    }

    public FeatureSet Extract(IProjectStore store, Photo photo, Settings settings, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var original = SKBitmap.Decode(store.PathFor(photo.Source))
            ?? throw new EchoException("Cannot decode image: " + photo.Source);
        var image = rasterImageService.ResizeGray(original, settings.FeatureWidth);
        var gray = image.Pixels;
        var width = image.Width;
        var height = image.Height;
        var candidates = DetectCandidates(gray, width, height, settings, cancellation);
        var occupied = ArrayPool<bool>.Shared.Rent(gray.Length);
        try
        {
            var occupancy = occupied.AsSpan(0, gray.Length);
            occupancy.Clear();
            return SelectFeatures(photo, settings, gray, width, height, candidates, occupancy, cancellation);
        }
        finally
        {
            ArrayPool<bool>.Shared.Return(occupied);
        }
    }

    private FeatureCandidates DetectCandidates(float[] gray, int width, int height, Settings settings, CancellationToken cancellation)
    {
        var candidates = new FeatureCandidates();
        if (!settings.DetectLines)
        {
            DetectPoints(candidates, gray, width, height, settings, cancellation);
            return candidates;
        }

        // Both branches only read gray and own separate output/scratch buffers. Junctions
        // depend on lines, so stay in that branch. Suppression happens after both complete.
        try
        {
            Parallel.Invoke(new ParallelOptions
            {
                CancellationToken = cancellation,
                MaxDegreeOfParallelism = Math.Min(2, Environment.ProcessorCount)
            },
                () => DetectPoints(candidates, gray, width, height, settings, cancellation),
                () =>
                {
                    candidates.Lines = lines.Detect(gray, width, height, settings.LineLimit, settings.LineMinimumLength, cancellation,
                        new LineDetectionOptions { GradientThreshold = settings.LineGradientThreshold, SplitRegions = settings.LineSplitRegions });
                    candidates.Junctions = lines.Junctions(candidates.Lines, gray, width, height, Math.Max(1, settings.FeatureLimit / 4), cancellation);
                });
        }
        catch (AggregateException exception)
        {
            // Parallel.Invoke has joined both branches before returning. Keep cancellation
            // and single detector failures consistent with the sequential API.
            cancellation.ThrowIfCancellationRequested();
            ExceptionDispatchInfo.Capture(exception.GetBaseException()).Throw();
            throw;
        }

        return candidates;
    }

    private void DetectPoints(FeatureCandidates candidates, float[] gray, int width, int height, Settings settings, CancellationToken cancellation)
    {
        if (settings.ScaleInvariantFeatures)
        {
            try
            {
                candidates.ScaleFeatures = ScaleSpaceFeatures.Detect(gray, width, height, settings.FeatureLimit, cancellation, compute, settings.ComputeBackend);
            }
            catch (ComputeException) when (settings.ComputeBackend == ComputeBackend.Auto)
            {
                candidates.ScaleFeatures = ScaleSpaceFeatures.Detect(gray, width, height, settings.FeatureLimit, cancellation);
            }
        }
        else
        {
            candidates.Corners = DetectCorners(gray, width, height, cancellation);
        }
    }

    private FeatureSet SelectFeatures(Photo photo, Settings settings, float[] gray, int width, int height,
        FeatureCandidates candidates, Span<bool> occupied, CancellationToken cancellation)
    {
        var corners = candidates.Corners;
        var output = new FeatureSet
        {
            ImageId = photo.Id
        };
        if (settings.DetectLines)
        {
            output.Lines = candidates.Lines.ToList();
            foreach (var point in candidates.Junctions)
            {
                Occupy(occupied, width, (int)Math.Round(point.X), (int)Math.Round(point.Y));
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

        if (settings.ScaleInvariantFeatures)
        {
            foreach (var feature in candidates.ScaleFeatures)
            {
                if (output.Features.Count >= settings.FeatureLimit)
                {
                    break;
                }

                feature.X *= (double)photo.Width / width;
                feature.Y *= (double)photo.Height / height;
                output.Features.Add(feature);
            }

            return output;
        }

        // Match the previous stable score ordering, including equal-score corners.
        corners.Sort(CompareCorners);
        Span<float> descriptor = stackalloc float[64];
        foreach (var corner in corners)
        {
            cancellation.ThrowIfCancellationRequested();
            if (occupied[corner.Y * width + corner.X])
            {
                continue;
            }

            if (!Describe(gray, width, corner, descriptor))
            {
                continue;
            }

            output.Features.Add(new Feature
            {
                X = (double)corner.X * photo.Width / width,
                Y = (double)corner.Y * photo.Height / height,
                Descriptor = descriptor.ToArray()
            });
            Occupy(occupied, width, corner.X, corner.Y);
            if (output.Features.Count >= settings.FeatureLimit)
            {
                break;
            }
        }

        return output;
    }

    private List<Corner> DetectCorners(ReadOnlySpan<float> gray, int width, int height, CancellationToken cancellation)
    {
        // Only five gradient-product rows are needed by the Harris window. Compute
        // each product once, retaining the original float products and sum order.
        var rowCount = 5;
        var planeLength = rowCount * width;
        var buffer = ArrayPool<float>.Shared.Rent(planeLength * 3);
        try
        {
            var xxRows = buffer.AsSpan(0, planeLength);
            var yyRows = buffer.AsSpan(planeLength, planeLength);
            var xyRows = buffer.AsSpan(planeLength * 2, planeLength);
            var corners = new List<Corner>();
            var nextRow = 10;
            Span<int> offsets = stackalloc int[5];
            for (var y = 12; y < height - 12; y += 2)
            {
                cancellation.ThrowIfCancellationRequested();
                for (; nextRow <= y + 2; nextRow++)
                {
                    var sourceRow = nextRow * width;
                    var targetRow = nextRow % rowCount * width;
                    for (var x = 1; x < width - 1; x++)
                    {
                        var index = sourceRow + x;
                        var gx = (gray[index + 1] - gray[index - 1]) * .5f;
                        var gy = (gray[index + width] - gray[index - width]) * .5f;
                        xxRows[targetRow + x] = gx * gx;
                        yyRows[targetRow + x] = gy * gy;
                        xyRows[targetRow + x] = gx * gy;
                    }
                }

                for (var row = 0; row < rowCount; row++)
                {
                    offsets[row] = (y + row - 2) % rowCount * width;
                }

                for (var x = 12; x < width - 12; x += 2)
                {
                    double xx = 0;
                    double yy = 0;
                    double xy = 0;
                    foreach (var offset in offsets)
                    {
                        var end = offset + x + 2;
                        for (var index = offset + x - 2; index <= end; index++)
                        {
                            xx += xxRows[index];
                            yy += yyRows[index];
                            xy += xyRows[index];
                        }
                    }

                    var score = xx * yy - xy * xy - .04 * (xx + yy) * (xx + yy);
                    if (score > .00001)
                    {
                        corners.Add(new Corner { X = x, Y = y, Score = score });
                    }
                }
            }

            return corners;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(buffer);
        }
    }

    private int CompareCorners(Corner left, Corner right)
    {
        var scoreOrder = right.Score.CompareTo(left.Score);
        if (scoreOrder != 0)
        {
            return scoreOrder;
        }

        var rowOrder = left.Y.CompareTo(right.Y);
        return rowOrder != 0 ? rowOrder : left.X.CompareTo(right.X);
    }

    private bool Describe(ReadOnlySpan<float> gray, int width, Corner corner, Span<float> descriptor)
    {
        var index = 0;
        double sum = 0;
        for (var y = -7; y <= 7; y += 2)
        {
            for (var x = -7; x <= 7; x += 2)
            {
                var value = gray[(corner.Y + y) * width + corner.X + x];
                descriptor[index++] = value;
                sum += value;
            }
        }

        // Enumerable.Average/Sum on float sequences return a float after double
        // accumulation. Preserve those roundings so saved descriptors stay identical.
        var mean = (float)(sum / descriptor.Length);
        double squared = 0;
        foreach (var value in descriptor)
        {
            var difference = value - mean;
            squared += difference * difference;
        }

        var norm = Math.Sqrt((float)squared);
        if (norm < .05)
        {
            return false;
        }

        for (var i = 0; i < descriptor.Length; i++)
        {
            descriptor[i] = (float)((descriptor[i] - mean) / norm);
        }

        return true;
    }

    private void Occupy(Span<bool> occupied, int width, int x, int y)
    {
        for (var dy = -7; dy <= 7; dy++)
        {
            occupied.Slice((y + dy) * width + x - 7, 15).Fill(true);
        }
    }
}
