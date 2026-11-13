using Echo.Contracts;

namespace Echo.Depth;

public interface IDepthNeighbourSelector
{
    CameraPose[] Select(ReconstructionScene scene, CameraPose reference, double medianDepth, int limit, CancellationToken cancellation);
}
