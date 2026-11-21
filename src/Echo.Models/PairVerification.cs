namespace Echo.Models;

public sealed class PairVerification
{
    public int AlgorithmVersion { get; set; } = 1;
    public string? Fingerprint
    {
        get; set;
    }
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
