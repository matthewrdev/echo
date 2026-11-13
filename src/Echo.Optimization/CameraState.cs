using Echo.Contracts;

namespace Echo.Optimization;

// Bundle steps replace the centre/rotation arrays; a rejected step can restore
// their previous references without serializing or duplicating camera objects.
internal readonly record struct CameraState(double[] Centre, double[] Rotation, double FocalPixels)
{
    public CameraState(CameraPose camera) : this(camera.Centre, camera.Rotation, camera.FocalPixels)
    {
    }

    public void Restore(CameraPose camera)
    {
        camera.Centre = Centre;
        camera.Rotation = Rotation;
        camera.FocalPixels = FocalPixels;
    }
}
