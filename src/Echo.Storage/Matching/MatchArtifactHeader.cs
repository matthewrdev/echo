using Echo.Models;

namespace Echo.Storage.Matching;

public sealed class MatchArtifactHeader
{
    public string Storage { get; set; } = "";
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public string Payload { get; set; } = "";
    public int PointCount
    {
        get; set;
    }
    public int LineCount
    {
        get; set;
    }
    public List<string> Kinds { get; set; } = [];
    public MatchingDiagnostics? Diagnostics
    {
        get; set;
    }
    public PairVerification? Verification
    {
        get; set;
    }
}
