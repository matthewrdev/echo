using System.Text.Json.Serialization;

namespace Echo.Models;

public sealed class ImagePair
{
    [JsonIgnore]
    public PairGeometryVerification? BundleVerification
    {
        get; set;
    }
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
