using Echo.Contracts;

namespace Echo.Simplification;

public interface IEdgeCollapse
{
    Surface Reduce(Surface input, int target, CancellationToken cancellation);
}
