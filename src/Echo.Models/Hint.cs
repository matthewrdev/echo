namespace Echo.Models;

public sealed class Hint
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = "camera";
    public string ImageId { get; set; } = "";
    public string? OtherImageId
    {
        get; set;
    }
    public GeoPosition? Position
    {
        get; set;
    }
    public Attitude? Attitude
    {
        get; set;
    }
    public double[]? Pixel
    {
        get; set;
    }
    public double[]? OtherPixel
    {
        get; set;
    }
    public bool Enabled { get; set; } = true;
    public string Note { get; set; } = "";
}
