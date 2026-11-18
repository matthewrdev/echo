using System.Text.Json.Serialization;

namespace Echo.Export;

public sealed class RedpointReferencePoint
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = "";
    [JsonPropertyName("Label")]
    public string Label { get; set; } = "";
    [JsonPropertyName("SceneX")]
    public double SceneX
    {
        get; set;
    }
    [JsonPropertyName("SceneY")]
    public double SceneY
    {
        get; set;
    }
    [JsonPropertyName("SceneZ")]
    public double SceneZ
    {
        get; set;
    }
    [JsonPropertyName("Latitude")]
    public double Latitude
    {
        get; set;
    }
    [JsonPropertyName("Longitude")]
    public double Longitude
    {
        get; set;
    }
    [JsonPropertyName("Elevation")]
    public double? Elevation
    {
        get; set;
    }
}
