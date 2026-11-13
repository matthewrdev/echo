namespace Echo.Contracts;

public sealed class ImagePair
{
    public PairVerification? Verification
    {
        get; set;
    }
    public List<LineMatch> LineMatches { get; set; } = [];
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public MatchingDiagnostics? Diagnostics
    {
        get; set;
    }
    public List<Match> Matches { get; set; } = [];
}
