using Echo.Contracts;
namespace Echo.Reconstruction;

public interface IRotationRefiner
{
    void Refine(CameraPose camera, List<CameraPose> cameras, Dictionary<string, Photo> images, List<ImagePair> pairs);
}
