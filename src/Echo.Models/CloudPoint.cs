namespace Echo.Models;

public sealed class CloudPoint
{
    public double[] Position { get; set; } = [];
    public string ImageA { get; set; } = "";
    public string ImageB { get; set; } = "";
    public double[] PixelA { get; set; } = [];
    public double[] PixelB { get; set; } = [];
    public double Error
    {
        get; set;
    }
    public byte[]? Colour
    {
        get; set;
    }
}
