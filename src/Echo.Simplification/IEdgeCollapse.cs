using Echo.Models;
using Echo.Storage.Resources;
using Echo.Storage;

namespace Echo.Simplification;

public interface IEdgeCollapse
{
    Surface ReduceTextured(Surface sourceMesh, int targetFaceCount, IList<double[]> uvs,
        IReadOnlyList<bool> lockedVertices, IList<double[]> outputUvs, CancellationToken cancellation);

    Surface Reduce(Surface sourceMesh, int targetFaceCount, CancellationToken cancellation, double detailTolerance = 0, IReadOnlyList<double>? vertexTolerances = null, WorkBuffers? work = null, Action<OperationProgress>? progress = null);
}
