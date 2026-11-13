using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Geometry;
using MathNet.Numerics.LinearAlgebra.Double;

namespace Echo.Reconstruction;

[Export(typeof(ICameraRegistrar)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class CameraRegistrar : ICameraRegistrar
{
    private readonly ICameraGeometry cameraGeometry;

    [ImportingConstructor]
    public CameraRegistrar(ICameraGeometry cameraGeometry)
    {
        this.cameraGeometry = cameraGeometry;
    }

    public CameraPose? Register(Photo photo, List<ImagePair> pairs, List<CloudPoint> points)
    {
        return Register(photo, CollectObservations(photo.Id, pairs, points));
    }

    private List<Observation> CollectObservations(string id, List<ImagePair> pairs, List<CloudPoint> points)
    {
        var lookup = new Dictionary<string, double[]>();
        string Key(string image, double[] pixel) => $"{image}:{Math.Round(pixel[0], 1)}:{Math.Round(pixel[1], 1)}";
        foreach (var point in points)
        {
            lookup.TryAdd(Key(point.ImageA, point.PixelA), point.Position);
            lookup.TryAdd(Key(point.ImageB, point.PixelB), point.Position);
        }

        var result = new Dictionary<string, Observation>();
        foreach (var pair in pairs.Where(p => p.A == id || p.B == id))
        {
            foreach (var match in pair.Matches)
            {
                var ownA = pair.A == id;
                var own = ownA ? match.A : match.B;
                var other = ownA ? match.B : match.A;
                if (lookup.TryGetValue(Key(ownA ? pair.B : pair.A, other), out var point))
                {
                    result.TryAdd(Key(id, own), new Observation
                    {
                        Pixel = own,
                        Point = point
                    });
                }
            }
        }

        return result.Values.ToList();
    }

    private CameraPose? Register(Photo photo, List<Observation> observations)
    {
        if (observations.Count < 8)
        {
            return null;
        }

        var random = new Random(93);
        CameraPose? best = null;
        List<Observation> bestInliers = [];
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var pose = FitPose(photo, observations.OrderBy(_ => random.Next()).Take(8).ToArray());
            if (pose == null)
            {
                continue;
            }

            var projection = new CameraProjector(cameraGeometry, photo, pose);
            var inliers = observations.Where(o =>
            {
                var p = projection.Project(new V3(o.Point));
                return p.HasValue && Math.Sqrt(cameraGeometry.Square(p.Value.X - o.Pixel[0]) + cameraGeometry.Square(p.Value.Y - o.Pixel[1])) < 10;
            }).ToList();
            if (inliers.Count > bestInliers.Count)
            {
                best = pose;
                bestInliers = inliers;
            }
        }

        return bestInliers.Count >= 8 ? FitPose(photo, bestInliers) ?? best : null;
    }

    private CameraPose? FitPose(Photo photo, IReadOnlyList<Observation> observations)
    {
        var mean = observations.Select(o => new V3(o.Point)).Aggregate(new V3(), (a, b) => a + b) / observations.Count;
        var scale = observations.Average(o => (new V3(o.Point) - mean).Length);
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
            var ray = cameraGeometry.Ray(photo, observations[i].Pixel);
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
        var magnitude = svd.S.Average();
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
            Centre = (new V3(centreNormal.ToArray()) * scale + mean).Array
        };
    }
}
