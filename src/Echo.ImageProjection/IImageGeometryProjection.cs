namespace Echo.ImageProjection;

public interface IImageGeometryProjection
{
    ImageGeometryResult Project(ImageGeometryRequest request, CancellationToken cancellation);
}
