using Echo.Contracts;

namespace Echo.Telemetry;

internal readonly record struct PerformanceStatisticKey(string Stage, string Name, PerformanceSpanKind Kind, PerformanceBackend Backend);
