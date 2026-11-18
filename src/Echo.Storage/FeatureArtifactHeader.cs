namespace Echo.Storage;

public sealed class FeatureArtifactHeader
{
    public string Storage { get; set; } = "";
    public string ImageId { get; set; } = "";
    public string Payload { get; set; } = "";
    public int PointCount
    {
        get; set;
    }
    public int LineCount
    {
        get; set;
    }
    public int DescriptorCount
    {
        get; set;
    }
    public List<string> Kinds { get; set; } = [];
}
