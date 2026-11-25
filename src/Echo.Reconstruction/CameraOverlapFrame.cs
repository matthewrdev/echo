using Echo.Geometry;
using Echo.Models;

namespace Echo.Reconstruction;

internal readonly record struct CameraOverlapFrame(V3 Centre, V3 Right, V3 Down, V3 Forward, double FocalPixels)
{
    public const int ParameterCount = 6;

    public CameraOverlapFrame(CameraPose pose) : this(new V3(pose.Centre),
        new V3(pose.Rotation[0], pose.Rotation[3], pose.Rotation[6]),
        new V3(pose.Rotation[1], pose.Rotation[4], pose.Rotation[7]),
        new V3(pose.Rotation[2], pose.Rotation[5], pose.Rotation[8]), pose.FocalPixels)
    {
    }

    public V3 Ray(V3 pixel) => (Right * pixel.X + Down * pixel.Y) / FocalPixels + Forward;

    public CameraOverlapFrame Rotate(V3 rotation)
    {
        var angle = rotation.Length;
        if (angle < 1e-12)
        {
            return this;
        }

        var axis = rotation / angle;
        var sine = Math.Sin(angle);
        var cosine = Math.Cos(angle);
        V3 Apply(V3 vector) =>
            vector * cosine + axis.Cross(vector) * sine + axis * (axis.Dot(vector) * (1 - cosine));

        return this with
        {
            Right = Apply(Right),
            Down = Apply(Down),
            Forward = Apply(Forward)
        };
    }

    public CameraPose ToPose(CameraPose source) => new()
    {
        ImageId = source.ImageId,
        Centre = Centre.Array,
        Rotation = [Right.X, Down.X, Forward.X, Right.Y, Down.Y, Forward.Y, Right.Z, Down.Z, Forward.Z],
        Source = source.Source,
        FocalPixels = FocalPixels
    };
}
