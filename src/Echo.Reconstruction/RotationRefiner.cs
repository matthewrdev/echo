using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Geometry;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace Echo.Reconstruction;

[Export(typeof(IRotationRefiner)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class RotationRefiner : IRotationRefiner
{
    private readonly ICameraGeometry cameraGeometry;

    [ImportingConstructor]
    public RotationRefiner(ICameraGeometry cameraGeometry)
    {
        this.cameraGeometry = cameraGeometry;
    }

    private double Residual(CameraPose camera, RotationObservation observation)
    {
        var ray = cameraGeometry.Rotate(camera.Rotation, observation.OwnRay).Unit;
        var other = cameraGeometry.Rotate(observation.Other.Rotation, observation.OtherRay).Unit;
        var normal = (new V3(camera.Centre) - new V3(observation.Other.Centre)).Cross(other).Unit;
        return (ray).Dot(normal);
    }

    public void Refine(CameraPose camera, List<CameraPose> cameras, Dictionary<string, Photo> images, List<ImagePair> pairs)
    {
        var observations = new List<RotationObservation>();
        foreach (var pair in pairs.Where(p => p.A == camera.ImageId || p.B == camera.ImageId))
        {
            var ownA = pair.A == camera.ImageId;
            var otherId = ownA ? pair.B : pair.A;
            var other = cameras.Find(c => c.ImageId == otherId);
            if (other == null || (new V3(other.Centre) - new V3(camera.Centre)).Length < .1)
            {
                continue;
            }

            foreach (var match in pair.Matches)
            {
                observations.Add(new RotationObservation
                {
                    Other = other,
                    OwnRay = cameraGeometry.Ray(images[camera.ImageId], ownA ? match.A : match.B),
                    OtherRay = cameraGeometry.Ray(images[otherId], ownA ? match.B : match.A)
                });
            }
        }

        if (observations.Count < 12)
        {
            return;
        }

        Matrix<double> normal = DenseMatrix.CreateIdentity(3) * .02;
        Vector<double> rhs = DenseVector.Create(3, 0);
        foreach (var o in observations)
        {
            var residual = Residual(camera, o);
            if (Math.Abs(residual) > .08)
            {
                continue;
            }

            var original = camera.Rotation;
            var jacobian = DenseVector.Create(3, 0);
            for (var axis = 0; axis < 3; axis++)
            {
                camera.Rotation = cameraGeometry.Multiply(SmallRotation(axis, 1e-5), original);
                jacobian[axis] = (Residual(camera, o) - residual) / 1e-5;
            }

            camera.Rotation = original;
            var weight = Math.Min(1, .006 / Math.Max(1e-10, Math.Abs(residual)));
            normal += jacobian.OuterProduct(jacobian) * weight;
            rhs -= jacobian * (weight * residual);
        }

        var step = normal.Solve(rhs);
        for (var axis = 0; axis < 3; axis++)
        {
            camera.Rotation = cameraGeometry.Multiply(SmallRotation(axis, Math.Clamp(step[axis], -.01, .01)), camera.Rotation);
        }

        camera.Source = "metadata + limited epipolar rotation refinement";
    }

    private double[] SmallRotation(int axis, double angle)
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
