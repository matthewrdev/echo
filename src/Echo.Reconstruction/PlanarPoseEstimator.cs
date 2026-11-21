using Echo.Models;
using Echo.Geometry;
using MathNet.Numerics.LinearAlgebra.Double;

namespace Echo.Reconstruction;

internal static class PlanarPoseEstimator
{
    public static CameraPose? Fit(Photo photo, IReadOnlyList<Observation> observations)
    {
        // Coplanar 3D points make the general DLT camera system rank deficient.
        // Estimate a calibrated homography in the fitted plane's coordinates,
        // then recover an orthonormal camera rotation and translation.
        var mean = new V3();
        foreach (var observation in observations)
        {
            mean += new V3(observation.Point);
        }

        mean /= observations.Count;
        var covariance = DenseMatrix.Create(3, 3, 0);
        foreach (var observation in observations)
        {
            var d = (new V3(observation.Point) - mean).Array;
            for (var i = 0; i < 3; i++)
            {
                for (var j = 0; j < 3; j++)
                {
                    covariance[i, j] += d[i] * d[j];
                }
            }
        }

        var plane = covariance.Svd(true);
        if (plane.S[1] < 1e-10 || plane.S[2] > plane.S[1] * .01)
        {
            return null;
        }

        var first = new V3(plane.U.Column(0).ToArray());
        var second = new V3(plane.U.Column(1).ToArray());
        var normal = first.Cross(second);
        var basis = DenseMatrix.OfColumnArrays(first.Array, second.Array, normal.Array);
        var design = DenseMatrix.Create(observations.Count * 2, 9, 0);
        for (var i = 0; i < observations.Count; i++)
        {
            var d = new V3(observations[i].Point) - mean;
            var u = d.Dot(first);
            var v = d.Dot(second);
            var ray = CameraGeometry.Ray(photo, observations[i].Pixel);
            double[] p = [u, v, 1];
            for (var j = 0; j < 3; j++)
            {
                design[i * 2, j] = p[j];
                design[i * 2, j + 6] = -ray.X * p[j];
                design[i * 2 + 1, j + 3] = p[j];
                design[i * 2 + 1, j + 6] = -ray.Y * p[j];
            }
        }

        var vector = design.Svd(true).VT.Row(8);
        var h = DenseMatrix.Create(3, 3, (r, c) => vector[r * 3 + c]);
        var magnitude = (h.Column(0).L2Norm() + h.Column(1).L2Norm()) * .5;
        if (magnitude < 1e-10)
        {
            return null;
        }
        // The plane origin must lie in front of the camera.
        var scale = (h[2, 2] >= 0 ? 1 : -1) / magnitude;
        var r1 = new V3((h.Column(0) * scale).ToArray());
        var r2 = new V3((h.Column(1) * scale).ToArray());
        var approximate = DenseMatrix.OfColumnArrays(r1.Array, r2.Array, r1.Cross(r2).Array);
        var rotationSvd = approximate.Svd(true);
        var uRotation = rotationSvd.U;
        if ((uRotation * rotationSvd.VT).Determinant() < 0)
        {
            uRotation.SetColumn(2, -uRotation.Column(2));
        }

        var rotation = uRotation * rotationSvd.VT * basis.Transpose();
        var translation = h.Column(2) * scale - rotation * DenseVector.OfArray(mean.Array);
        return new CameraPose
        {
            ImageId = photo.Id,
            Source = "visual planar registration",
            Rotation = rotation.Transpose().ToRowMajorArray(),
            Centre = (-rotation.TransposeThisAndMultiply(translation)).ToArray()
        };
    }
}
