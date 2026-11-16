namespace Echo.Benchmarks;

internal sealed class CrossPassMeasurement
{
    public required string Method
    {
        get; init;
    }
    public required int FeatureWidth
    {
        get; init;
    }
    public required int FeatureLimit
    {
        get; init;
    }
    public required string Left
    {
        get; init;
    }
    public required string Right
    {
        get; init;
    }
    public required int LeftFeatures
    {
        get; init;
    }
    public required int RightFeatures
    {
        get; init;
    }
    public required int Candidates
    {
        get; init;
    }
    public required int Inliers
    {
        get; init;
    }
    public required int FundamentalInliers
    {
        get; init;
    }
    public required int RobustIterations
    {
        get; init;
    }
    public required int RecoveredMatches
    {
        get; init;
    }
    public required double ExtractionSeconds
    {
        get; init;
    }
    public required long ExtractionBytes
    {
        get; init;
    }
    public required double MatchingSeconds
    {
        get; init;
    }
    public required double VerificationSeconds
    {
        get; init;
    }
}
