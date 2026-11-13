namespace Echo.Contracts;

public sealed class PairVerification
{
    public int AlgorithmVersion { get; set; } = 1;
    public double ThresholdPixels
    {
        get; set;
    }
    public int CandidateCount
    {
        get; set;
    }
    public int InlierCount
    {
        get; set;
    }
}
