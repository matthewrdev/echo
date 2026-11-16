using Echo.Contracts;
using Echo.Storage;

namespace Echo.Simplification;

public interface IEdgeCollapse
{
    Surface Reduce(Surface sourceMesh, int targetFaceCount, CancellationToken cancellation, double detailTolerance = 0, IReadOnlyList<double>? vertexTolerances = null, WorkBuffers? work = null, Action<OperationProgress>? progress = null);
}
