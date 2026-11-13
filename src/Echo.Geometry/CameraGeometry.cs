using System.ComponentModel.Composition;
using Echo.Contracts;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace Echo.Geometry;

[Export(typeof(ICameraGeometry)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class CameraGeometry : ICameraGeometry
{

    [ImportingConstructor]
    public CameraGeometry()
    {
    }

    public V3 Rotate(double[] r, V3 p) => new(r[0] * p.X + r[1] * p.Y + r[2] * p.Z, r[3] * p.X + r[4] * p.Y + r[5] * p.Z, r[6] * p.X + r[7] * p.Y + r[8] * p.Z);

    public double[] Transpose(double[] r) => [r[0], r[3], r[6], r[1], r[4], r[7], r[2], r[5], r[8]];

    public double[] Multiply(double[] a, double[] b)
    {
        var r = new double[9];
        for (var i = 0; i < 3; i++)
        {
            for (var j = 0; j < 3; j++)
            {
                for (var k = 0; k < 3; k++)
                {
                    r[i * 3 + j] += a[i * 3 + k] * b[k * 3 + j];
                }
            }
        }

        return r;
    }

    public double[] AttitudeRotation(Attitude a)
    {
        var yaw = a.Yaw * Math.PI / 180;
        var pitch = a.Pitch * Math.PI / 180;
        var roll = a.Roll * Math.PI / 180;
        var forward = new V3(Math.Sin(yaw) * Math.Cos(pitch), Math.Sin(pitch), -Math.Cos(yaw) * Math.Cos(pitch));
        var right = new V3(Math.Cos(yaw), 0, Math.Sin(yaw));
        var down = (forward).Cross(right);
        var rolledRight = right * Math.Cos(roll) + down * Math.Sin(roll);
        var rolledDown = down * Math.Cos(roll) - right * Math.Sin(roll);
        return [rolledRight.X, rolledDown.X, forward.X, rolledRight.Y, rolledDown.Y, forward.Y, rolledRight.Z, rolledDown.Z, forward.Z];
    }

    // WGS84 ECEF -> local East/Up/South. Unknown vertical datum is retained in the sidecar.
    public V3 ToLocal(GeoPosition position, GeoPosition origin)
    {
        var delta = Ecef(position) - Ecef(origin);
        var lat = origin.Latitude * Math.PI / 180;
        var lon = origin.Longitude * Math.PI / 180;
        var east = -Math.Sin(lon) * delta.X + Math.Cos(lon) * delta.Y;
        var north = -Math.Sin(lat) * Math.Cos(lon) * delta.X - Math.Sin(lat) * Math.Sin(lon) * delta.Y + Math.Cos(lat) * delta.Z;
        var up = Math.Cos(lat) * Math.Cos(lon) * delta.X + Math.Cos(lat) * Math.Sin(lon) * delta.Y + Math.Sin(lat) * delta.Z;
        return new V3(east, up, -north);
    }

    public double[] LocalToEcefMatrix(GeoPosition origin)
    {
        var lat = origin.Latitude * Math.PI / 180;
        var lon = origin.Longitude * Math.PI / 180;
        var p = Ecef(origin);
        return [-Math.Sin(lon), Math.Cos(lat) * Math.Cos(lon), Math.Sin(lat) * Math.Cos(lon), p.X, Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat) * Math.Sin(lon), p.Y, 0, Math.Sin(lat), -Math.Cos(lat), p.Z, 0, 0, 0, 1];
    }

    public V3 Ecef(GeoPosition p)
    {
        const double a = 6378137;
        const double e2 = 6.69437999014e-3;
        var lat = p.Latitude * Math.PI / 180;
        var lon = p.Longitude * Math.PI / 180;
        var h = p.Altitude ?? 0;
        var n = a / Math.Sqrt(1 - e2 * Math.Sin(lat) * Math.Sin(lat));
        return new V3((n + h) * Math.Cos(lat) * Math.Cos(lon), (n + h) * Math.Cos(lat) * Math.Sin(lon), (n * (1 - e2) + h) * Math.Sin(lat));
    }

    public double Focal(Photo photo, CameraPose? camera = null) => camera?.FocalPixels > 0 ? camera.FocalPixels : photo.FocalPixels;

    public V3 Ray(Photo photo, double[] pixel, CameraPose? camera = null) => new((pixel[0] - photo.Width * .5) / Focal(photo, camera), (pixel[1] - photo.Height * .5) / Focal(photo, camera), 1);

    public double[]? Project(Photo photo, CameraPose pose, V3 point)
    {
        var p = Rotate(Transpose(pose.Rotation), point - new V3(pose.Centre));
        return p.Z > 1e-8 ? [Focal(photo, pose) * p.X / p.Z + photo.Width * .5, Focal(photo, pose) * p.Y / p.Z + photo.Height * .5, p.Z] : null;
    }

    public Triangulated? Triangulate(Photo a, Photo b, CameraPose ca, CameraPose cb, Match match, double maxError = 12)
    {
        var da = Rotate(ca.Rotation, Ray(a, match.A, ca)).Unit;
        var db = Rotate(cb.Rotation, Ray(b, match.B, cb)).Unit;
        var baseline = new V3(cb.Centre) - new V3(ca.Centre);
        var dot = (da).Dot(db);
        var det = 1 - dot * dot;
        if (det < .000025)
        {
            return null;
        }

        var ta = ((baseline).Dot(da) - dot * (baseline).Dot(db)) / det;
        var tb = (dot * (baseline).Dot(da) - (baseline).Dot(db)) / det;
        if (ta <= 0 || tb <= 0 || ta > 5000 || tb > 5000)
        {
            return null;
        }

        var point = (new V3(ca.Centre) + da * ta + new V3(cb.Centre) + db * tb) * .5;
        var pa = Project(a, ca, point);
        var pb = Project(b, cb, point);
        if (pa == null || pb == null)
        {
            return null;
        }

        var error = Math.Sqrt((Square(pa[0] - match.A[0]) + Square(pa[1] - match.A[1]) + Square(pb[0] - match.B[0]) + Square(pb[1] - match.B[1])) * .5);
        return error <= maxError ? new Triangulated
        {
            Point = point,
            Error = error
        }

        : null;
    }

    public double Square(double x) => x * x;

    private Matrix<double> Essential(Photo a, Photo b, IReadOnlyList<Match> matches)
    {
        var design = DenseMatrix.Create(matches.Count, 9, 0);
        for (var i = 0; i < matches.Count; i++)
        {
            var p = Ray(a, matches[i].A);
            var q = Ray(b, matches[i].B);
            design.SetRow(i, [q.X * p.X, q.X * p.Y, q.X, q.Y * p.X, q.Y * p.Y, q.Y, p.X, p.Y, 1]);
        }

        var vector = design.Svd(true).VT.Row(8);
        var e = DenseMatrix.Create(3, 3, (r, c) => vector[r * 3 + c]);
        var svd = e.Svd(true);
        return svd.U * DenseMatrix.OfDiagonalArray([1.0, 1.0, 0.0]) * svd.VT;
    }

    private double EpipolarError(Matrix<double> e, Photo a, Photo b, Match match)
    {
        var p = Vector<double>.Build.DenseOfArray(Ray(a, match.A).Array);
        var q = Vector<double>.Build.DenseOfArray(Ray(b, match.B).Array);
        var ep = e * p;
        var etq = e.TransposeThisAndMultiply(q);
        var numerator = q.DotProduct(ep);
        return numerator * numerator / Math.Max(1e-15, ep[0] * ep[0] + ep[1] * ep[1] + etq[0] * etq[0] + etq[1] * etq[1]);
    }

    public RelativePose? EstimateRelative(Photo a, Photo b, List<Match> matches)
    {
        if (matches.Count < 12)
        {
            return null;
        }

        var random = new Random(817);
        List<Match> best = [];
        var threshold = Square(3.0 * Math.Max(a.Width, b.Width) / 1000 / Math.Min(a.FocalPixels, b.FocalPixels));
        for (var iteration = 0; iteration < 160; iteration++)
        {
            var sample = matches.OrderBy(_ => random.Next()).Take(8).ToArray();
            var e = Essential(a, b, sample);
            var inliers = matches.Where(m => EpipolarError(e, a, b, m) < threshold).ToList();
            if (inliers.Count > best.Count)
            {
                best = inliers;
            }
        }

        if (best.Count < 12)
        {
            return null;
        }

        var final = Essential(a, b, best).Svd(true);
        var u = final.U;
        var vt = final.VT;
        if (u.Determinant() < 0)
        {
            u.SetColumn(2, -u.Column(2));
        }

        if (vt.Determinant() < 0)
        {
            vt.SetRow(2, -vt.Row(2));
        }

        var w = DenseMatrix.OfArray(new double[,] { { 0, -1, 0 }, { 1, 0, 0 }, { 0, 0, 1 } });
        RelativePose? result = null;
        var most = 0;
        foreach (var rotation in new[]
        {
            u * w * vt,
            u * w.Transpose() * vt
        }
        )
        {
            foreach (var sign in new[]
            {
                1.0,
                -1.0
            }
            )
            {
                var r = rotation.ToRowMajorArray();
                var t = (u.Column(2) * sign).ToArray();
                var poseB = new CameraPose
                {
                    Rotation = Transpose(r),
                    Centre = (Rotate(Transpose(r), new V3(t)) * -1).Array
                };
                var count = best.Count(m => Triangulate(a, b, new CameraPose(), poseB, m, 20) != null);
                if (count > most)
                {
                    most = count;
                    result = new RelativePose
                    {
                        Rotation = r,
                        Translation = t,
                        Inliers = best
                    };
                }
            }
        }

        return most >= 10 ? result : null;
    }
}
