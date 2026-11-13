using Echo.Contracts;

namespace Echo.Geometry;

public interface IFundamentalEstimator
{
    FundamentalEstimate? Estimate(IReadOnlyList<Match> matches, double thresholdPixels, CancellationToken cancellation);
}
