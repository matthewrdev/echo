namespace Echo.Contracts;

public sealed class PairGeometryVerification
{
    public required string Fingerprint
    {
        get; init;
    }
    public required List<Match> Inliers
    {
        get; init;
    }
}
