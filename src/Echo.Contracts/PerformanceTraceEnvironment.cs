namespace Echo.Contracts;

public sealed record PerformanceTraceEnvironment(string Runtime, string OperatingSystem, string Architecture,
    int LogicalProcessors, bool ServerGc);
