using System.ComponentModel.Composition;
using Echo.Contracts;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace Echo.Geometry;

[Export(typeof(ICameraGeometry)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class CameraGeometry : ICameraGeometry
{

    private readonly IRandomSampleSelector samples;

    [ImportingConstructor]
    public CameraGeometry(IRandomSampleSelector samples)
    {
        this.samples = samples;
    }

    public V3 Rotate(double[] rotation, V3 vector) => new(rotation[0] * vector.X + rotation[1] * vector.Y + rotation[2] * vector.Z, rotation[3] * vector.X + rotation[4] * vector.Y + rotation[5] * vector.Z, rotation[6] * vector.X + rotation[7] * vector.Y + rotation[8] * vector.Z);

    public double[] Transpose(double[] rotation) => [rotation[0], rotation[3], rotation[6], rotation[1], rotation[4], rotation[7], rotation[2], rotation[5], rotation[8]];

    public double[] Multiply(double[] leftMatrix, double[] rightMatrix)
    {
        var r = new double[9];
        Multiply(leftMatrix, rightMatrix, r);
        return r;
    }

    public void Multiply(ReadOnlySpan<double> leftMatrix, ReadOnlySpan<double> rightMatrix, Span<double> result)
    {
        result.Clear();
        for (var i = 0; i < 3; i++)
        {
            for (var j = 0; j < 3; j++)
            {
                for (var k = 0; k < 3; k++)
                {
                    result[i * 3 + j] += leftMatrix[i * 3 + k] * rightMatrix[k * 3 + j];
                }
            }
        }
    }

    public double[] AttitudeRotation(Attitude attitude)
    {
        var yaw = attitude.Yaw * Math.PI / 180;
        var pitch = attitude.Pitch * Math.PI / 180;
        var roll = attitude.Roll * Math.PI / 180;
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

    public V3 Ecef(GeoPosition position)
    {
        const double a = 6378137;
        const double e2 = 6.69437999014e-3;
        var lat = position.Latitude * Math.PI / 180;
        var lon = position.Longitude * Math.PI / 180;
        var h = position.Altitude ?? 0;
        var n = a / Math.Sqrt(1 - e2 * Math.Sin(lat) * Math.Sin(lat));
        return new V3((n + h) * Math.Cos(lat) * Math.Cos(lon), (n + h) * Math.Cos(lat) * Math.Sin(lon), (n * (1 - e2) + h) * Math.Sin(lat));
    }

    public double Focal(Photo photo, CameraPose? camera = null) => camera?.FocalPixels > 0 ? camera.FocalPixels : photo.FocalPixels;

    public V3 Ray(Photo photo, double[] pixel, CameraPose? camera = null) => new((pixel[0] - photo.Width * .5) / Focal(photo, camera), (pixel[1] - photo.Height * .5) / Focal(photo, camera), 1);

    public double[]? Project(Photo photo, CameraPose pose, V3 point) => ProjectPoint(photo, pose, point)?.Array;

    public V3? ProjectPoint(Photo photo, CameraPose pose, V3 point)
    {
        var delta = point - new V3(pose.Centre);
        var rotation = pose.Rotation;
        var p = new V3(
            rotation[0] * delta.X + rotation[3] * delta.Y + rotation[6] * delta.Z,
            rotation[1] * delta.X + rotation[4] * delta.Y + rotation[7] * delta.Z,
            rotation[2] * delta.X + rotation[5] * delta.Y + rotation[8] * delta.Z);
        return p.Z > 1e-8 ? new V3(Focal(photo, pose) * p.X / p.Z + photo.Width * .5, Focal(photo, pose) * p.Y / p.Z + photo.Height * .5, p.Z) : null;
    }

    public Triangulated? Triangulate(Photo leftPhoto, Photo rightPhoto, CameraPose leftCamera, CameraPose rightCamera, Match match, double maximumReprojectionErrorPixels = 12)
    {
        var da = Rotate(leftCamera.Rotation, Ray(leftPhoto, match.A, leftCamera)).Unit;
        var db = Rotate(rightCamera.Rotation, Ray(rightPhoto, match.B, rightCamera)).Unit;
        var baseline = new V3(rightCamera.Centre) - new V3(leftCamera.Centre);
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

        var point = (new V3(leftCamera.Centre) + da * ta + new V3(rightCamera.Centre) + db * tb) * .5;
        var pa = ProjectPoint(leftPhoto, leftCamera, point);
        var pb = ProjectPoint(rightPhoto, rightCamera, point);
        if (pa == null || pb == null)
        {
            return null;
        }

        var error = Math.Sqrt((Square(pa.Value.X - match.A[0]) + Square(pa.Value.Y - match.A[1]) + Square(pb.Value.X - match.B[0]) + Square(pb.Value.Y - match.B[1])) * .5);
        return error <= maximumReprojectionErrorPixels ? new Triangulated
        {
            Point = point,
            Error = error
        }

        : null;
    }

    public double Square(double x) => x * x;

    private Matrix<double> Essential(Photo leftPhoto, Photo rightPhoto, IReadOnlyList<Match> matches)
    {
        var design = DenseMatrix.Create(matches.Count, 9, 0);
        for (var i = 0; i < matches.Count; i++)
        {
            var p = Ray(leftPhoto, matches[i].A);
            var q = Ray(rightPhoto, matches[i].B);
            design[i, 0] = q.X * p.X;
            design[i, 1] = q.X * p.Y;
            design[i, 2] = q.X;
            design[i, 3] = q.Y * p.X;
            design[i, 4] = q.Y * p.Y;
            design[i, 5] = q.Y;
            design[i, 6] = p.X;
            design[i, 7] = p.Y;
            design[i, 8] = 1;
        }

        var vector = design.Svd(true).VT.Row(8);
        var e = DenseMatrix.Create(3, 3, (r, c) => vector[r * 3 + c]);
        var svd = e.Svd(true);
        return svd.U * DenseMatrix.OfDiagonalArray([1.0, 1.0, 0.0]) * svd.VT;
    }

    private double EpipolarError(Matrix<double> essentialMatrix, Photo leftPhoto, Photo rightPhoto, Match match, EpipolarWorkspace workspace)
    {
        var first = Ray(leftPhoto, match.A);
        var second = Ray(rightPhoto, match.B);
        var p = workspace.First;
        var q = workspace.Second;
        p[0] = first.X;
        p[1] = first.Y;
        p[2] = first.Z;
        q[0] = second.X;
        q[1] = second.Y;
        q[2] = second.Z;
        var ep = workspace.Forward;
        var etq = workspace.Reverse;
        essentialMatrix.Multiply(p, ep);
        essentialMatrix.TransposeThisAndMultiply(q, etq);
        var numerator = q.DotProduct(ep);
        return numerator * numerator / Math.Max(1e-15, ep[0] * ep[0] + ep[1] * ep[1] + etq[0] * etq[0] + etq[1] * etq[1]);
    }

    public RelativePose? EstimateRelative(Photo leftPhoto, Photo rightPhoto, List<Match> matches)
    {
        if (matches.Count < 12)
        {
            return null;
        }

        var random = new Random(817);
        var best = new List<Match>();
        var inliers = new List<Match>(matches.Count);
        var sample = new Match[8];
        var workspace = new EpipolarWorkspace();
        var threshold = Square(3.0 * Math.Max(leftPhoto.Width, rightPhoto.Width) / 1000 / Math.Min(leftPhoto.FocalPixels, rightPhoto.FocalPixels));
        for (var iteration = 0; iteration < 160; iteration++)
        {
            samples.Select<Match>(matches, random, sample);
            var e = Essential(leftPhoto, rightPhoto, sample);
            inliers.Clear();
            foreach (var match in matches)
            {
                if (EpipolarError(e, leftPhoto, rightPhoto, match, workspace) < threshold)
                {
                    inliers.Add(match);
                }
            }
            if (inliers.Count > best.Count)
            {
                var previous = best;
                best = inliers;
                inliers = previous;
            }
        }

        if (best.Count < 12)
        {
            return null;
        }

        var final = Essential(leftPhoto, rightPhoto, best).Svd(true);
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
                var inverse = Transpose(r);
                var poseA = new CameraPose();
                var poseB = new CameraPose
                {
                    Rotation = inverse,
                    Centre = (Rotate(inverse, new V3(t)) * -1).Array
                };
                var count = 0;
                foreach (var match in best)
                {
                    if (Triangulate(leftPhoto, rightPhoto, poseA, poseB, match, 20) != null)
                    {
                        count++;
                    }
                }
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
