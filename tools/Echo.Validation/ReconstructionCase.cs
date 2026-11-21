using Echo.Models;

namespace Echo.Validation;

public sealed class ReconstructionCase
{
    public required string Name
    {
        get; set;
    }
    public required string Dataset
    {
        get; set;
    }
    public required string PhotoManifestSha256
    {
        get; set;
    }
    public required List<string> PhotoHashes
    {
        get; set;
    }
    public required Settings Settings
    {
        get; set;
    }
    public required Dictionary<QualityMetric, MetricBounds> Limits
    {
        get; set;
    }
    public required string BaselineDescription
    {
        get; set;
    }
}
