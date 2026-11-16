namespace Echo.Contracts;

public sealed class SemanticMap
{
    private IReadOnlyList<byte>? vegetationSamples;
    private IReadOnlyList<byte>? rockSamples;

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<byte> VegetationSamples
    {
        get => vegetationSamples ?? Vegetation;
        set => vegetationSamples = value;
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<byte> RockSamples
    {
        get => rockSamples ?? Rock;
        set => rockSamples = value;
    }

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
