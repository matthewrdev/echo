using Echo.Depth;

namespace Echo.Tests;

internal sealed class TestDepthCostAccelerator : IDepthCostAccelerator
{
    private readonly string mode;

    public TestDepthCostAccelerator(string mode)
    {
        this.mode = mode;
    }

    public string Backend => "metal";
    public List<TestDepthCostSession> Sessions { get; } = [];

    public IDepthCostSession Open(StereoView[] views, CancellationToken cancellation)
    {
        if (mode == "unavailable")
        {
            throw new DepthAccelerationException("Test device unavailable");
        }
        var session = new TestDepthCostSession(mode);
        Sessions.Add(session);
        return session;
    }
}
