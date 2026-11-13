using Echo.Contracts;
namespace Echo.Reconstruction;

public interface ICameraRegistrar
{
    CameraPose? Register(Photo photo, List<ImagePair> pairs, List<CloudPoint> points);
}
