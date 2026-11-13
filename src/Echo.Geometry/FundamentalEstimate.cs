using Echo.Contracts;

namespace Echo.Geometry;

public sealed class FundamentalEstimate
{
    public double[] Matrix { get; set; } = [];
    public List<Match> Inliers { get; set; } = [];
    public int Iterations
    {
        get; set;
    }
}
