namespace Echo.Telemetry;

public sealed record PerformanceTraceExport(string Path, string Format, string RunId, string AttemptId,
    int RetainedSpans, long DroppedSpans, long Bytes);
