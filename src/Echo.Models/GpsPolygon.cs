using System.Text.Json.Serialization;

namespace Echo.Models;

public sealed class GpsPolygon
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonRequired]
    public CropRegionMode Mode { get; set; } = CropRegionMode.Exclude;
    public List<GpsPolygonVertex> Vertices { get; set; } = [];
}
