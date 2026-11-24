namespace Echo.Pipeline.Exporting;

public sealed record RunExchangeResult(string RunId, string Directory, IReadOnlyList<string> Files);
