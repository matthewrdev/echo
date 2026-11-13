using Echo.Contracts;
namespace Echo.Pipeline;

public interface IImagePairSelector
{
    IReadOnlyList<PairIdentity> Select(ProjectData project, IReadOnlyList<Photo> images, IReadOnlyDictionary<string, string> featureUnits);
}
