namespace Echo.Validation;

public sealed record QualityCheck(
    QualityMetric Metric,
    double? Actual,
    double Minimum,
    double Maximum,
    bool Passed);
