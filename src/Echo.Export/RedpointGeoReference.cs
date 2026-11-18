using System.Text.Json.Serialization;

namespace Echo.Export;

public sealed class RedpointGeoReference
{
    [JsonPropertyName("Version")]
    public int Version { get; set; } = 1;
    [JsonPropertyName("Enabled")]
    public bool Enabled { get; set; } = true;
    [JsonPropertyName("ReferencePoints")]
    public List<RedpointReferencePoint> ReferencePoints { get; set; } = [];
}
