using Echo.Models;
namespace Echo.Matching;

public sealed record DescriptorMatches(List<DescriptorCorrespondence> Matches, MatchingDiagnostics Diagnostics);
