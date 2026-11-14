using Echo.Depth;
using Echo.Geometry;

namespace Echo.Tests;

internal sealed class TestDepthCostSession : IDepthCostSession
{
    private readonly string mode;
    private int calls;

    public TestDepthCostSession(string mode)
    {
        this.mode = mode;
    }

    public string Device => "Test GPU";
    public string Implementation => "deliberately incorrect test costs";
    public bool Disposed
    {
        get; private set;
    }

    public float[] Calculate(V3 centre, IReadOnlyList<DepthSearchSample> samples, CancellationToken cancellation)
    {
        var call = Interlocked.Increment(ref calls);
        if (mode == "failure" || mode == "partial-failure" && call > 1)
        {
            throw new DepthAccelerationException("Test command failed");
        }
        if (mode == "cancel")
        {
            throw new OperationCanceledException();
        }
        if (mode == "invalid-count")
        {
            return [];
        }
        var costs = new float[samples.Sum(sample => sample.Hypotheses.Length)];
        var value = mode switch
        {
            "nan" => float.NaN,
            "negative" => -1,
            "too-large" => 3,
            "reject" or "partial-failure" => float.PositiveInfinity,
            _ => 0
        };
        Array.Fill(costs, value);
        return costs;
    }

    public void Dispose()
    {
        Disposed = true;
    }
}
