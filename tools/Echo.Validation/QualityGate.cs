namespace Echo.Validation;

public static class QualityGate
{
    public static void Validate(IReadOnlyDictionary<QualityMetric, MetricBounds> limits)
    {
        if (limits.Count != Enum.GetValues<QualityMetric>().Length)
        {
            throw new InvalidDataException("Every reconstruction metric must have an explicit acceptance interval.");
        }
        foreach (var metric in Enum.GetValues<QualityMetric>())
        {
            if (!limits.TryGetValue(metric, out var bounds) || bounds == null)
            {
                throw new InvalidDataException($"Missing acceptance interval: {metric}.");
            }
            var finite = double.IsFinite(bounds.Minimum) && double.IsFinite(bounds.Maximum);
            if (!finite || bounds.Minimum < 0 || bounds.Maximum < bounds.Minimum)
            {
                throw new InvalidDataException($"Invalid acceptance interval: {metric}.");
            }
        }
    }

    public static List<QualityCheck> Evaluate(IReadOnlyDictionary<QualityMetric, double> measurements,
        IReadOnlyDictionary<QualityMetric, MetricBounds> limits)
    {
        Validate(limits);
        var checks = new List<QualityCheck>();
        foreach (var metric in Enum.GetValues<QualityMetric>())
        {
            var bounds = limits[metric];
            var measured = measurements.TryGetValue(metric, out var value) && double.IsFinite(value);
            var passed = measured && value >= bounds.Minimum && value <= bounds.Maximum;
            checks.Add(new QualityCheck(metric, measured ? value : null, bounds.Minimum, bounds.Maximum, passed));
        }
        return checks;
    }
}
