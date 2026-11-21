using Echo.Models;
namespace Echo.Lines;

public interface ILineDetector
{
    IReadOnlyList<LineFeature> Detect(float[] gray, int width, int height, int limit, double minimumLength, CancellationToken cancellation, LineDetectionOptions? options = null);
    IReadOnlyList<Feature> Junctions(IReadOnlyList<LineFeature> lines, float[] gray, int width, int height, int limit, CancellationToken cancellation);
}
