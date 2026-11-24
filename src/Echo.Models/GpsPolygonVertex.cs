using System.Text.Json.Serialization;

namespace Echo.Models;

public sealed record GpsPolygonVertex(
    [property: JsonRequired] double Latitude,
    [property: JsonRequired] double Longitude);
