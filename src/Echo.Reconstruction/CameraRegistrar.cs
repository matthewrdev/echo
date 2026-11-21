using Echo.Models;
using Echo.Geometry;
using MathNet.Numerics.LinearAlgebra.Double;

namespace Echo.Reconstruction;

public static class CameraRegistrar
{
    public static CameraPose? Register(Photo photo, List<ImagePair> pairs, IEnumerable<CloudPoint> points,
        Random random, CancellationToken cancellation = default)
    {
        return Register(photo, CollectObservations(photo.Id, pairs, points, cancellation), random, cancellation);
    }

    private static List<Observation> CollectObservations(string imageId, List<ImagePair> pairs, IEnumerable<CloudPoint> points, CancellationToken cancellation)
    {
        var lookup = new Dictionary<string, double[]>();
        string Key(string image, double[] pixel) => $"{image}:{Math.Round(pixel[0], 1)}:{Math.Round(pixel[1], 1)}";
        foreach (var point in points)
        {
            cancellation.ThrowIfCancellationRequested();
            lookup.TryAdd(Key(point.ImageA, point.PixelA), point.Position);
            lookup.TryAdd(Key(point.ImageB, point.PixelB), point.Position);
        }

        var result = new Dictionary<string, Observation>();
        foreach (var pair in pairs)
        {
            cancellation.ThrowIfCancellationRequested();
            if (pair.A != imageId && pair.B != imageId)
            {
                continue;
            }
            foreach (var match in pair.Matches)
            {
                var ownA = pair.A == imageId;
                var own = ownA ? match.A : match.B;
                var other = ownA ? match.B : match.A;
                if (lookup.TryGetValue(Key(ownA ? pair.B : pair.A, other), out var point))
                {
                    result.TryAdd(Key(imageId, own), new Observation
                    {
                        Pixel = own,
                        Point = point
                    });
                }
            }
        }

        var observations = new List<Observation>(result.Count);
        foreach (var observation in result.Values)
        {
            observations.Add(observation);
        }
        return observations;
    }

    private static CameraPose? Register(Photo photo, List<Observation> observations, Random random, CancellationToken cancellation)
    {
        if (observations.Count < 30)
        {
            return null;
        }

        CameraPose? best = null;
        var bestInliers = new List<Observation>();
        var inliers = new List<Observation>(observations.Count);
        var sample = new Observation[6];
        var indices = Enumerable.Range(0, observations.Count).ToArray();
        var maximumIterations = 8192;
        for (var iteration = 0; iteration < maximumIterations; iteration++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var slot = 0; slot < sample.Length; slot++)
            {
                var selected = random.Next(slot, indices.Length);
                var previous = indices[slot];
                indices[slot] = indices[selected];
                indices[selected] = previous;
                sample[slot] = observations[indices[slot]];
            }
            foreach (var pose in new[] { FitPose(photo, sample), PlanarPoseEstimator.Fit(photo, sample) })
            {
                if (pose == null)
                {
                    continue;
                }

                var projection = new CameraProjector(photo, pose);
                inliers.Clear();
                foreach (var observation in observations)
                {
                    var p = projection.Project(new V3(observation.Point));
                    if (p.HasValue && Math.Sqrt(CameraGeometry.Square(p.Value.X - observation.Pixel[0]) + CameraGeometry.Square(p.Value.Y - observation.Pixel[1])) < 10)
                    {
                        inliers.Add(observation);
                    }
                }
                if (inliers.Count > bestInliers.Count)
                {
                    best = pose;
                    var previous = bestInliers;
                    bestInliers = inliers;
                    inliers = previous;
                    var ratio = (double)bestInliers.Count / observations.Count;
                    var miss = Math.Clamp(1 - Math.Pow(ratio, sample.Length), 1e-12, 1 - 1e-12);
                    var required = (int)Math.Min(8192, Math.Ceiling(Math.Log(.001) / Math.Log(miss)));
                    maximumIterations = Math.Min(maximumIterations, Math.Max(128, required));
                }
            }
        }

        if (bestInliers.Count < 30 || bestInliers.Count < observations.Count * .25)
        {
            return null;
        }
        return best?.Source == "visual planar registration"
            ? PlanarPoseEstimator.Fit(photo, bestInliers) ?? best
            : FitPose(photo, bestInliers) ?? best;
    }

    private static CameraPose? FitPose(Photo photo, IReadOnlyList<Observation> observations)
    {
        var mean = new V3();
        foreach (var observation in observations)
        {
            mean += new V3(observation.Point);
        }
        mean /= observations.Count;
        double scale = 0;
        foreach (var observation in observations)
        {
            scale += (new V3(observation.Point) - mean).Length;
        }
        scale /= observations.Count;
        if (scale < 1e-8)
        {
            return null;
        }

        var design = DenseMatrix.Create(observations.Count * 2, 12, 0);
        Span<double> x = stackalloc double[4];
        x[3] = 1;
        for (var i = 0; i < observations.Count; i++)
        {
            var p = (new V3(observations[i].Point) - mean) / scale;
            var ray = CameraGeometry.Ray(photo, observations[i].Pixel);
            x[0] = p.X;
            x[1] = p.Y;
            x[2] = p.Z;
            for (var j = 0; j < 4; j++)
            {
                design[2 * i, j] = x[j];
                design[2 * i, 8 + j] = -ray.X * x[j];
                design[2 * i + 1, 4 + j] = x[j];
                design[2 * i + 1, 8 + j] = -ray.Y * x[j];
            }
        }

        var vector = design.Svd(true).VT.Row(11);
        var pmat = DenseMatrix.Create(3, 4, (r, c) => vector[r * 4 + c]);
        var rot = pmat.SubMatrix(0, 3, 0, 3);
        if (rot.Determinant() < 0)
        {
            pmat *= -1;
            rot *= -1;
        }

        var svd = rot.Svd(true);
        var magnitude = (svd.S[0] + svd.S[1] + svd.S[2]) / 3;
        if (magnitude < 1e-10)
        {
            return null;
        }

        var r = svd.U * svd.VT;
        if (r.Determinant() < 0)
        {
            return null;
        }

        var centreNormal = -r.TransposeThisAndMultiply(pmat.Column(3) / magnitude);
        return new CameraPose
        {
            ImageId = photo.Id,
            Source = "visual DLT registration",
            Rotation = r.Transpose().ToRowMajorArray(),
            Centre = (new V3(centreNormal[0], centreNormal[1], centreNormal[2]) * scale + mean).Array
        };
    }
}
