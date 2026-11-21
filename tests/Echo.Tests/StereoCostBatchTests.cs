using Echo.Depth;
using Echo.Geometry;
using Echo.Imaging;

namespace Echo.Tests;

public sealed class StereoCostBatchTests
{
    public StereoCostBatchTests()
    {
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("SIMD depth hypotheses preserve scalar costs across borders, rotations and missing support", () =>
        {
            var random = new Random(4182);
            var patch = new double[25];
            var finite = 0;
            var rejected = 0;
            foreach (var viewCount in new[] { 1, 2, 3, 12 })
            {
                var views = Enumerable.Range(0, viewCount).Select(index => View(random, index)).ToArray();
                for (var iteration = 0; iteration < 300; iteration++)
                {
                    var mean = 0.0;
                    for (var index = 0; index < patch.Length; index++)
                    {
                        patch[index] = (float)random.NextDouble();
                        mean += patch[index];
                    }

                    mean /= patch.Length;
                    var norm = 0.0;
                    for (var index = 0; index < patch.Length; index++)
                    {
                        patch[index] -= mean;
                        norm += patch[index] * patch[index];
                    }

                    var centre = new V3(.3, -.2, .1);
                    var ray = new V3(random.NextDouble() * 1.4 - .7, random.NextDouble() - .5, 1);
                    var firstDepth = .1 + random.NextDouble() * 20;
                    // Mix neighbouring and distant hypotheses, including behind-camera samples.
                    var secondDepth = iteration % 3 == 0 ? -firstDepth : firstDepth * (1 + random.NextDouble());
                    var pair = StereoCostBatch.Calculate(views, centre, ray, firstDepth, secondDepth, patch, norm);
                    var expectedFirst = Reference(views, centre + ray * firstDepth, firstDepth, patch, norm);
                    var expectedSecond = Reference(views, centre + ray * secondDepth, secondDepth, patch, norm);
                    CheckBits(pair.First, expectedFirst);
                    CheckBits(pair.Second, expectedSecond);
                    finite += double.IsFinite(expectedFirst) ? 1 : 0;
                    rejected += double.IsPositiveInfinity(expectedSecond) ? 1 : 0;
                }
            }

            Check(finite > 100 && rejected > 100, "Exercise successful sampling and invalid hypotheses");
        });
        tests.Run("SIMD rejects constant patches and cannot revive an invalid lane", () =>
        {
            var random = new Random(581);
            var views = new[] { View(random, 0), View(random, 1) };
            foreach (var view in views)
            {
                Array.Fill(view.Gray, .5f);
            }

            var patch = Enumerable.Range(0, 25).Select(index => index / 25.0 - .5).ToArray();
            var norm = patch.Sum(value => value * value);
            var costs = StereoCostBatch.Calculate(views, new V3(0, 0, 0), new V3(0, 0, 1), -5, 5, patch, norm);
            Check(double.IsPositiveInfinity(costs.First) && double.IsPositiveInfinity(costs.Second), "Missing variance or support must remain rejected");
        });
    }

    private StereoView View(Random random, int index)
    {
        var angle = (index - 1) * .025;
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        var rotation = new[] { cosine, 0, sine, 0, 1, 0, -sine, 0, cosine };
        return new StereoView
        {
            Centre = new V3((index - 1) * .15, index * .02, 0),
            Focal = 32 + index,
            Width = 40,
            Height = 32,
            Gray = Enumerable.Range(0, 40 * 32).Select(_ => (float)random.NextDouble()).ToArray(),
            WorldToCamera = rotation,
            PatchRight = CameraGeometry.Rotate(rotation, new V3(1.0 / 32, 0, 0)),
            PatchDown = CameraGeometry.Rotate(rotation, new V3(0, 1.0 / 32, 0))
        };
    }

    private double Reference(ReadOnlySpan<StereoView> views, V3 world, double depth, ReadOnlySpan<double> patch, double norm)
    {
        // Original scalar depth-cost calculation, including float sample-square rounding.
        var firstView = double.PositiveInfinity;
        var secondView = firstView;
        var count = 0;
        Span<V3> columns = stackalloc V3[5];
        foreach (var view in views)
        {
            var p = CameraGeometry.Rotate(view.WorldToCamera, world - view.Centre);
            if (p.Z <= 0)
            {
                continue;
            }

            double sum = 0;
            double squared = 0;
            double dot = 0;
            var n = 0;
            var valid = true;
            var sampler = new GrayscaleSampler(view.Gray, view.Width, view.Height);
            for (var column = 0; column < columns.Length; column++)
            {
                columns[column] = p + view.PatchRight * ((column - 2) * depth);
            }

            for (var dy = -2; dy <= 2 && valid; dy++)
            {
                var down = view.PatchDown * (dy * depth);
                for (var dx = -2; dx <= 2; dx++)
                {
                    var q = columns[dx + 2] + down;
                    var u = q.X / q.Z * view.Focal + view.Width * .5;
                    var v = q.Y / q.Z * view.Focal + view.Height * .5;
                    var sample = q.Z > 0 ? sampler.Sample(u, v) : float.NaN;
                    if (!float.IsFinite(sample))
                    {
                        valid = false;
                        break;
                    }

                    sum += sample;
                    squared += sample * sample;
                    dot += patch[n++] * sample;
                }
            }

            var variance = squared - sum * sum / 25;
            if (!valid || variance < .0002)
            {
                continue;
            }

            var cost = 1 - Math.Clamp(dot / Math.Sqrt(norm * variance), -1, 1);
            count++;
            if (cost < firstView)
            {
                secondView = firstView;
                firstView = cost;
            }
            else if (cost < secondView)
            {
                secondView = cost;
            }
        }

        return count < Math.Min(2, views.Length) ? double.PositiveInfinity : count == 1 ? firstView : (firstView + secondView) * .5;
    }

    private void CheckBits(double actual, double expected)
    {
        Check(BitConverter.DoubleToInt64Bits(actual) == BitConverter.DoubleToInt64Bits(expected), $"Stereo cost changed: {expected:R} to {actual:R}");
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
