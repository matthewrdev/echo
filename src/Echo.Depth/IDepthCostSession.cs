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

    // Write one row-major cost per sample/hypothesis and return the written count.
    // Calls may arrive concurrently. Input and output buffers belong to the caller
    // and must not be retained after this synchronous call completes.
    int Calculate(V3 centre, IReadOnlyList<DepthSearchSample> samples, float[] costs, CancellationToken cancellation);
}
