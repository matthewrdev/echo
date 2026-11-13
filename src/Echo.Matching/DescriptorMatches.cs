using Echo.Contracts;
namespace Echo.Matching;

public sealed record DescriptorMatches(List<DescriptorCorrespondence> Matches, MatchingDiagnostics Diagnostics);
