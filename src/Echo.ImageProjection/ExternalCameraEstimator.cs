using Echo.Geometry;
using Echo.Models;
using Echo.Reconstruction;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace Echo.ImageProjection;

internal static class ExternalCameraEstimator
{
    public static EstimatedCamera? Estimate(Photo photo, IReadOnlyList<RegistrationMatch> matches,
        double? focalPixels, bool controls, CancellationToken cancellation)
    {
        if (matches.Count < 6)
        {
            return null;
        }

        var threshold = ErrorThreshold(photo);
        CameraPose? best = null;
        var inliers = new List<RegistrationMatch>();
        var random = new Random(2419);
        var sample = new RegistrationMatch[6];
        var limit = controls ? 256 : 8192;
        for (var attempt = -1; attempt < limit; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            var pool = Math.Min(matches.Count, 18 + (attempt + 1) / 24);
            if (attempt >= 0)
            {
                Sample(matches, pool, random, sample);
            }

            var pose = Fit(photo, attempt < 0 ? matches : sample, focalPixels);
            if (pose == null)
            {
                continue;
            }

            var agreeing = Agreeing(photo, pose, matches, threshold);
            if (agreeing.Count <= inliers.Count)
            {
                continue;
            }

            best = pose;
            inliers = agreeing;
            var fraction = (double)agreeing.Count / matches.Count;
            var miss = Math.Clamp(1 - Math.Pow(fraction, 6), 1e-12, 1 - 1e-12);
            var required = Math.Ceiling(Math.Log(.001) / Math.Log(miss));
            limit = Math.Min(limit, (int)Math.Clamp(required, Math.Min(96, limit), limit));
            if (agreeing.Count == matches.Count)
            {
                break;
            }
        }

        var minimum = controls ? Math.Max(6, (matches.Count + 1) / 2) : 12;
        if (best == null || inliers.Count < minimum)
        {
            return null;
        }

        for (var iteration = 0; iteration < 3; iteration++)
        {
            var candidate = Refine(photo, best, inliers, focalPixels != null, threshold);
            var agreeing = Agreeing(photo, candidate, matches, threshold);
            if (agreeing.Count < minimum)
            {
                break;
            }

            best = candidate;
            inliers = agreeing;
        }

        var horizontalSpan = inliers.Max(item => item.Pixel[0]) - inliers.Min(item => item.Pixel[0]);
        var verticalSpan = inliers.Max(item => item.Pixel[1]) - inliers.Min(item => item.Pixel[1]);
        if (horizontalSpan < photo.Width * .06 || verticalSpan < photo.Height * .06)
        {
            return null;
        }

        var projection = new CameraProjector(photo, best);
        var errors = inliers.Select(item => Error(projection, item)).Order().ToArray();
        return new EstimatedCamera(best, inliers, errors[errors.Length / 2]);
    }

    public static double ErrorThreshold(Photo photo) => Math.Max(3, photo.Width * 6.0 / 1400);

    public static double Error(CameraProjector projection, RegistrationMatch match)
    {
        var point = projection.Project(match.Position);
        return point is { } p
            ? Math.Sqrt(CameraGeometry.Square(p.X - match.Pixel[0]) + CameraGeometry.Square(p.Y - match.Pixel[1]))
            : double.PositiveInfinity;
    }

    private static void Sample(IReadOnlyList<RegistrationMatch> matches, int count, Random random, Span<RegistrationMatch> sample)
    {
        Span<int> indices = stackalloc int[6];
        for (var i = 0; i < sample.Length; i++)
        {
            int index;
            do
            {
                index = random.Next(count);
            }
            while (indices[..i].Contains(index));

            indices[i] = index;
            sample[i] = matches[index];
        }
    }

    private static List<RegistrationMatch> Agreeing(Photo photo, CameraPose pose,
        IReadOnlyList<RegistrationMatch> matches, double threshold)
    {
        var projection = new CameraProjector(photo, pose);
        return matches.Where(match => Error(projection, match) < threshold).ToList();
    }

    private static CameraPose? Fit(Photo photo, IReadOnlyList<RegistrationMatch> matches, double? focalPixels)
    {
        var mean = matches.Aggregate(default(V3), (sum, point) => sum + point.Position) / matches.Count;
        var scale = matches.Average(point => (point.Position - mean).Length);
        if (scale < 1e-10)
        {
            return null;
        }

        var imageScale = focalPixels ?? photo.Width;
        var design = DenseMatrix.Create(matches.Count * 2, 12, 0);
        Span<double> coordinates = stackalloc double[4];
        coordinates[3] = 1;
        for (var i = 0; i < matches.Count; i++)
        {
            var point = (matches[i].Position - mean) / scale;
            coordinates[0] = point.X;
            coordinates[1] = point.Y;
            coordinates[2] = point.Z;
            var x = (matches[i].Pixel[0] - photo.Width * .5) / imageScale;
            var y = (matches[i].Pixel[1] - photo.Height * .5) / imageScale;
            for (var j = 0; j < coordinates.Length; j++)
            {
                design[i * 2, j] = coordinates[j];
                design[i * 2 + 1, 4 + j] = coordinates[j];
                design[i * 2, 8 + j] = -x * coordinates[j];
                design[i * 2 + 1, 8 + j] = -y * coordinates[j];
            }
        }

        var svd = design.Svd(true);
        if (svd.S[10] < svd.S[0] * 1e-9)
        {
            if (focalPixels == null)
            {
                return null;
            }

            var planarPhoto = new Photo { Id = photo.Id, Width = photo.Width, Height = photo.Height, FocalPixels = focalPixels.Value };
            var pose = CameraRegistrar.FitPlanar(planarPhoto, matches.Select(match =>
                new CameraControlPoint(match.Pixel, match.Position)).ToArray());
            if (pose != null)
            {
                pose.FocalPixels = focalPixels.Value;
            }

            return pose;
        }

        var coefficients = svd.VT.Row(11);
        var xAxis = new V3(coefficients[0], coefficients[1], coefficients[2]);
        var yAxis = new V3(coefficients[4], coefficients[5], coefficients[6]);
        var zAxis = new V3(coefficients[8], coefficients[9], coefficients[10]);
        var magnitude = zAxis.Length;
        if (magnitude < 1e-10)
        {
            return null;
        }

        var focal = focalPixels == null ? (xAxis.Length + yAxis.Length) / (2 * magnitude) : 1;
        if (focalPixels == null && (focal < .22 || focal > 3))
        {
            return null;
        }

        var sign = xAxis.Cross(yAxis).Dot(zAxis) < 0 ? -1 : 1;
        var rotation = DenseMatrix.OfRowArrays((xAxis / focal * sign).Array, (yAxis / focal * sign).Array, (zAxis * sign).Array);
        var decomposition = rotation.Svd(true);
        var worldToCamera = decomposition.U * decomposition.VT;
        var offset = new V3(coefficients[3] / focal, coefficients[7] / focal, coefficients[11]) * (sign * scale / magnitude);
        return Pose(photo, worldToCamera, mean, offset, focal * imageScale);
    }

    private static CameraPose Refine(Photo photo, CameraPose start, IReadOnlyList<RegistrationMatch> matches,
        bool fixedFocal, double threshold)
    {
        var origin = matches.Aggregate(default(V3), (sum, point) => sum + point.Position) / matches.Count;
        Matrix<double> rotation = DenseMatrix.Create(3, 3, (row, column) => start.Rotation[column * 3 + row]);
        var offset = new V3((rotation * DenseVector.OfArray((origin - new V3(start.Centre)).Array)).ToArray());
        var focal = start.FocalPixels;
        var damping = .001;
        var current = start;
        var cost = Cost(photo, current, matches, threshold);
        var count = fixedFocal ? 6 : 7;
        for (var iteration = 0; iteration < 16; iteration++)
        {
            var normal = DenseMatrix.Create(count, count, 0);
            var right = DenseVector.Create(count, 0);
            foreach (var match in matches)
            {
                var relative = new V3((rotation * DenseVector.OfArray((match.Position - origin).Array)).ToArray());
                var position = relative + offset;
                if (position.Z <= 1e-8)
                {
                    continue;
                }

                var x = focal * position.X / position.Z;
                var y = focal * position.Y / position.Z;
                var residualX = match.Pixel[0] - photo.Width * .5 - x;
                var residualY = match.Pixel[1] - photo.Height * .5 - y;
                var dx = new V3(focal / position.Z, 0, -x / position.Z);
                var dy = new V3(0, focal / position.Z, -y / position.Z);
                var ax = relative.Cross(dx);
                var ay = relative.Cross(dy);
                double[] jx = [ax.X, ax.Y, ax.Z, dx.X, dx.Y, dx.Z, x];
                double[] jy = [ay.X, ay.Y, ay.Z, dy.X, dy.Y, dy.Z, y];
                var weight = Math.Min(1, threshold / Math.Max(1e-8, Math.Sqrt(residualX * residualX + residualY * residualY)));
                for (var row = 0; row < count; row++)
                {
                    right[row] += weight * (jx[row] * residualX + jy[row] * residualY);
                    for (var column = 0; column < count; column++)
                    {
                        normal[row, column] += weight * (jx[row] * jx[column] + jy[row] * jy[column]);
                    }
                }
            }

            for (var axis = 0; axis < count; axis++)
            {
                normal[axis, axis] += damping * Math.Max(1, normal[axis, axis]);
            }

            var delta = normal.Solve(right);
            var nextRotation = RotationStep(new V3(delta[0], delta[1], delta[2])) * rotation;
            var nextOffset = offset + new V3(delta[3], delta[4], delta[5]);
            var nextFocal = fixedFocal ? focal : Math.Clamp(focal * Math.Exp(Math.Clamp(delta[6], -.3, .3)),
                photo.Width * .22, photo.Width * 3);
            var candidate = Pose(photo, nextRotation, origin, nextOffset, nextFocal);
            var nextCost = Cost(photo, candidate, matches, threshold);
            if (nextCost < cost)
            {
                current = candidate;
                cost = nextCost;
                rotation = nextRotation;
                offset = nextOffset;
                focal = nextFocal;
                damping *= .4;
            }
            else
            {
                damping *= 4;
            }
        }

        return current;
    }

    private static double Cost(Photo photo, CameraPose pose, IReadOnlyList<RegistrationMatch> matches, double threshold)
    {
        var projector = new CameraProjector(photo, pose);
        return matches.Sum(match =>
        {
            var error = Error(projector, match);
            return error <= threshold ? error * error : threshold * (2 * error - threshold);
        });
    }

    private static Matrix<double> RotationStep(V3 rotation)
    {
        var angle = rotation.Length;
        var axis = angle > 1e-12 ? rotation / angle : rotation;
        var skew = DenseMatrix.OfArray(new double[,] { { 0, -axis.Z, axis.Y }, { axis.Z, 0, -axis.X }, { -axis.Y, axis.X, 0 } });
        return DenseMatrix.CreateIdentity(3) + skew * Math.Sin(angle) + skew * skew * (1 - Math.Cos(angle));
    }

    private static CameraPose Pose(Photo photo, Matrix<double> worldToCamera, V3 origin, V3 offset, double focal)
    {
        var rotation = worldToCamera.Transpose();
        return new CameraPose
        {
            ImageId = photo.Id,
            Centre = (origin - new V3((rotation * DenseVector.OfArray(offset.Array)).ToArray())).Array,
            Rotation = rotation.ToRowMajorArray(),
            FocalPixels = focal,
            Source = "external image registration"
        };
    }
}
