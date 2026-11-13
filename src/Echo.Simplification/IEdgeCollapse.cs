using Echo.Contracts;

namespace Echo.Simplification;

public interface IEdgeCollapse
{
    Surface Reduce(Surface sourceMesh, int targetFaceCount, CancellationToken cancellation);
}
