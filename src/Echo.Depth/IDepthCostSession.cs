using Echo.Geometry;

namespace Echo.Depth;

public interface IDepthCostSession : IDisposable
{
    string Device
    {
        get;
    }
    string Implementation
    {
        get;
    }

    // One row-major cost per sample/hypothesis. Calls may arrive concurrently.
    float[] Calculate(V3 centre, IReadOnlyList<DepthSearchSample> samples, CancellationToken cancellation);
}
