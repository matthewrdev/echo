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
}
