using Echo.Models;

namespace Echo.Pipeline.Previews;

public sealed record StageCameraInputs(string Basis, IReadOnlyList<string> ImageIds,
    IReadOnlyList<MatchingIndexEntry>? Pairs = null);
