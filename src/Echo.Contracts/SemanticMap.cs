namespace Echo.Contracts;

public sealed class SemanticMap
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
    public byte[] Vegetation { get; set; } = [];
    public byte[] Rock { get; set; } = [];
}
