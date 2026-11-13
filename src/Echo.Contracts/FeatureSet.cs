namespace Echo.Contracts;

public sealed class FeatureSet
{
    public List<LineFeature> Lines { get; set; } = [];
    public string ImageId { get; set; } = "";
    public List<Feature> Features { get; set; } = [];
}
