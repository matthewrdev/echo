namespace Echo.Contracts;

public sealed class DepthMap
{
    public string ImageId { get; set; } = "";
    public int Width
    {
        get; set;
    }
    public int Height
    {
        get; set;
    }
    public float[] Depth { get; set; } = [];
    public float[] Confidence { get; set; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public IDepthSamples? Samples
    {
        get; set;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public int SampleCount => Samples?.Count ?? Depth.Length;

    public float DepthAt(int index) => Samples?.DepthAt(index) ?? Depth[index];

    public void ReadDepth(int start, Span<float> destination)
    {
        if (Samples != null)
        {
            Samples.ReadDepth(start, destination);
        }
        else
        {
            Depth.AsSpan(start, destination.Length).CopyTo(destination);
        }
    }

    public float ConfidenceAt(int index) => Samples?.ConfidenceAt(index) ?? (Confidence.Length == 0 ? 0 : Confidence[index]);
}
