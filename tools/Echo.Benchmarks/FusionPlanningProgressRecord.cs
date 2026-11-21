using Echo.Models;

namespace Echo.Benchmarks;

public sealed record FusionPlanningProgressRecord(
    double Seconds,
    OperationProgress Progress);
