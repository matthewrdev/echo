using System.Runtime.Intrinsics;
using Echo.Geometry;
using Echo.Imaging;

namespace Echo.Depth;

internal static class StereoCostBatch
{
    public static StereoCostPair Calculate(ReadOnlySpan<StereoView> views, V3 centre, V3 ray,
        double firstDepth, double secondDepth, ReadOnlySpan<double> patch, double norm)
    {
        // Lanes are independent hypotheses, never partial sums of one hypothesis.
        // Preserve every scalar multiply/add and the order of all 25 samples.
        var depths = Vector128.Create(firstDepth, secondDepth);
        var worldX = Vector128.Create(centre.X) + Vector128.Create(ray.X) * depths;
        var worldY = Vector128.Create(centre.Y) + Vector128.Create(ray.Y) * depths;
        var worldZ = Vector128.Create(centre.Z) + Vector128.Create(ray.Z) * depths;
        Span<Vector128<double>> columnsX = stackalloc Vector128<double>[5];
        Span<Vector128<double>> columnsY = stackalloc Vector128<double>[5];
        Span<Vector128<double>> columnsZ = stackalloc Vector128<double>[5];
        Span<double> first = stackalloc double[2];
        Span<double> second = stackalloc double[2];
        Span<int> counts = stackalloc int[2];
        first.Fill(double.PositiveInfinity);
        second.Fill(double.PositiveInfinity);
        counts.Clear();
        foreach (var view in views)
        {
            var x = worldX - Vector128.Create(view.Centre.X);
            var y = worldY - Vector128.Create(view.Centre.Y);
            var z = worldZ - Vector128.Create(view.Centre.Z);
            var rotation = view.WorldToCamera;
            var px = Vector128.Create(rotation[0]) * x + Vector128.Create(rotation[1]) * y + Vector128.Create(rotation[2]) * z;
            var py = Vector128.Create(rotation[3]) * x + Vector128.Create(rotation[4]) * y + Vector128.Create(rotation[5]) * z;
            var pz = Vector128.Create(rotation[6]) * x + Vector128.Create(rotation[7]) * y + Vector128.Create(rotation[8]) * z;
            var validFirst = pz.GetElement(0) > 0;
            var validSecond = pz.GetElement(1) > 0;
            if (!validFirst && !validSecond)
            {
                continue;
            }

            var sum = Vector128<double>.Zero;
            var squared = Vector128<double>.Zero;
            var dot = Vector128<double>.Zero;
            var sampler = new GrayscaleSampler(view.Gray, view.Width, view.Height);
            var focal = Vector128.Create(view.Focal);
            var halfWidth = Vector128.Create(view.Width * .5);
            var halfHeight = Vector128.Create(view.Height * .5);
            for (var column = 0; column < 5; column++)
            {
                var distance = Vector128.Create((double)(column - 2)) * depths;
                columnsX[column] = px + Vector128.Create(view.PatchRight.X) * distance;
                columnsY[column] = py + Vector128.Create(view.PatchRight.Y) * distance;
                columnsZ[column] = pz + Vector128.Create(view.PatchRight.Z) * distance;
            }
            var sampleIndex = 0;
            for (var row = -2; row <= 2; row++)
            {
                if (!validFirst && !validSecond)
                {
                    break;
                }
                var distance = Vector128.Create((double)row) * depths;
                var downX = Vector128.Create(view.PatchDown.X) * distance;
                var downY = Vector128.Create(view.PatchDown.Y) * distance;
                var downZ = Vector128.Create(view.PatchDown.Z) * distance;
                for (var column = 0; column < 5; column++)
                {
                    var qz = columnsZ[column] + downZ;
                    var u = (columnsX[column] + downX) / qz * focal + halfWidth;
                    var v = (columnsY[column] + downY) / qz * focal + halfHeight;
                    var sampleFirst = validFirst && qz.GetElement(0) > 0
                        ? sampler.Sample(u.GetElement(0), v.GetElement(0)) : float.NaN;
                    var sampleSecond = validSecond && qz.GetElement(1) > 0
                        ? sampler.Sample(u.GetElement(1), v.GetElement(1)) : float.NaN;
                    validFirst = float.IsFinite(sampleFirst);
                    validSecond = float.IsFinite(sampleSecond);
                    if (!validFirst && !validSecond)
                    {
                        break;
                    }
                    var samples = Vector128.Create((double)sampleFirst, (double)sampleSecond);
                    sum += samples;
                    // The original squared sample rounds in float before double accumulation.
                    squared += Vector128.Create((double)(sampleFirst * sampleFirst), (double)(sampleSecond * sampleSecond));
                    dot += Vector128.Create(patch[sampleIndex++]) * samples;
                }
            }
            for (var lane = 0; lane < 2; lane++)
            {
                var valid = lane == 0 ? validFirst : validSecond;
                var total = sum.GetElement(lane);
                var variance = squared.GetElement(lane) - total * total / 25;
                if (!valid || variance < .0002)
                {
                    continue;
                }
                var cost = 1 - Math.Clamp(dot.GetElement(lane) / Math.Sqrt(norm * variance), -1, 1);
                counts[lane]++;
                if (cost < first[lane])
                {
                    second[lane] = first[lane];
                    first[lane] = cost;
                }
                else if (cost < second[lane])
                {
                    second[lane] = cost;
                }
            }
        }
        var minimumSupport = Math.Min(2, views.Length);
        var firstCost = counts[0] < minimumSupport ? double.PositiveInfinity : counts[0] == 1 ? first[0] : (first[0] + second[0]) * .5;
        var secondCost = counts[1] < minimumSupport ? double.PositiveInfinity : counts[1] == 1 ? first[1] : (first[1] + second[1]) * .5;
        return new StereoCostPair(firstCost, secondCost);
    }
}
