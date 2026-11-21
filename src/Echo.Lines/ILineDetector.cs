using Echo.Models;
namespace Echo.Lines;

public interface ILineDetector
{
    List<LineFeature> Detect(float[] gray, int width, int height, int limit, double minimumLength, CancellationToken cancellation, LineDetectionOptions? options = null);
    List<Feature> Junctions(IReadOnlyList<LineFeature> lines, float[] gray, int width, int height, int limit, CancellationToken cancellation);
}
