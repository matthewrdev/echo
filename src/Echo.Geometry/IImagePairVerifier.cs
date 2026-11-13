using Echo.Contracts;

namespace Echo.Geometry;

public interface IImagePairVerifier
{
    ImagePair Verify(Photo leftPhoto, Photo rightPhoto, ImagePair candidates, CancellationToken cancellation);
}
