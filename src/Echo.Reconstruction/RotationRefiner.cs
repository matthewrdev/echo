using Echo.Contracts;
using Echo.Geometry;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace Echo.Reconstruction;

public static class RotationRefiner
{
    private static double Residual(CameraPose camera, RotationObservation observation)
    {
        var ray = CameraGeometry.Rotate(camera.Rotation, observation.OwnRay).Unit;
        var other = CameraGeometry.Rotate(observation.Other.Rotation, observation.OtherRay).Unit;
        var normal = (new V3(camera.Centre) - new V3(observation.Other.Centre)).Cross(other).Unit;
        return (ray).Dot(normal);
    }

    public static void Refine(CameraPose camera, List<CameraPose> cameras, Dictionary<string, Photo> images, List<ImagePair> pairs)
    {
        var observations = new List<RotationObservation>();
        var cameraLookup = new Dictionary<string, CameraPose>(cameras.Count, StringComparer.Ordinal);
        foreach (var candidate in cameras)
        {
            cameraLookup.Add(candidate.ImageId, candidate);
        }
        foreach (var pair in pairs)
        {
            if (pair.A != camera.ImageId && pair.B != camera.ImageId)
            {
                continue;
            }
            var ownA = pair.A == camera.ImageId;
            var otherId = ownA ? pair.B : pair.A;
            if (!cameraLookup.TryGetValue(otherId, out var other) || (new V3(other.Centre) - new V3(camera.Centre)).Length < .1)
            {
                continue;
            }

            foreach (var match in pair.Matches)
            {
                observations.Add(new RotationObservation
                {
                    Other = other,
                    OwnRay = CameraGeometry.Ray(images[camera.ImageId], ownA ? match.A : match.B),
                    OtherRay = CameraGeometry.Ray(images[otherId], ownA ? match.B : match.A)
                });
            }
        }

        if (observations.Count < 12)
        {
            return;
        }

        Matrix<double> normal = DenseMatrix.CreateIdentity(3) * .02;
        Vector<double> rhs = DenseVector.Create(3, 0);
        Span<double> jacobian = stackalloc double[3];
        var rotations = new[] { SmallRotation(0, 1e-5), SmallRotation(1, 1e-5), SmallRotation(2, 1e-5) };
        var perturbed = new double[9];
        foreach (var o in observations)
        {
            var residual = Residual(camera, o);
            if (Math.Abs(residual) > .08)
            {
                continue;
            }

            var original = camera.Rotation;
            try
            {
                for (var axis = 0; axis < 3; axis++)
                {
                    CameraGeometry.Multiply(rotations[axis], original, perturbed);
                    camera.Rotation = perturbed;
                    jacobian[axis] = (Residual(camera, o) - residual) / 1e-5;
                }
            }
            finally
            {
                camera.Rotation = original;
            }
            var weight = Math.Min(1, .006 / Math.Max(1e-10, Math.Abs(residual)));
            for (var row = 0; row < 3; row++)
            {
                for (var column = 0; column < 3; column++)
                {
                    normal[row, column] += jacobian[row] * jacobian[column] * weight;
                }
                rhs[row] -= jacobian[row] * (weight * residual);
            }
        }

        var step = normal.Solve(rhs);
        for (var axis = 0; axis < 3; axis++)
        {
            camera.Rotation = CameraGeometry.Multiply(SmallRotation(axis, Math.Clamp(step[axis], -.01, .01)), camera.Rotation);
        }

        camera.Source = "metadata + limited epipolar rotation refinement";
    }

    private static double[] SmallRotation(int axis, double angle)
    {
        var c = Math.Cos(angle);
        var s = Math.Sin(angle);
        return axis switch
        {
            0 => [1, 0, 0, 0, c, -s, 0, s, c],
            1 => [c, 0, s, 0, 1, 0, -s, 0, c],
            _ => [c, -s, 0, s, c, 0, 0, 0, 1]
        };
    }
}
