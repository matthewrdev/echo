namespace Echo.Contracts;

public sealed class MatchingDiagnostics
{
    public int FeaturesA
    {
        get; set;
    }
    public int FeaturesB
    {
        get; set;
    }
    public int Accepted
    {
        get; set;
    }
    public int RejectedNonMutual
    {
        get; set;
    }
    public int RejectedAmbiguous
    {
        get; set;
    }
    public int RejectedDistance
    {
        get; set;
    }
    public double RatioThreshold
    {
        get; set;
    }
    public double DistanceThreshold
    {
        get; set;
    }
}
