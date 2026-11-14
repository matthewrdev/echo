using Echo.Contracts;

namespace Echo.Geometry;

public sealed class FundamentalMatrixEstimate
{
    public double[] Matrix { get; set; } = [];
    public List<Match> Inliers { get; set; } = [];
    public int Iterations
    {
        get; set;
    }
}
