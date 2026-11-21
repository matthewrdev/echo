using System.Text.Json;

namespace Echo.Models;

public sealed class Photo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string Preview { get; set; } = "";
    public string Hash { get; set; } = "";
    public string SourceHash { get; set; } = "";
    public int Width
    {
        get; set;
    }
    public int Height
    {
        get; set;
    }
    public string Camera { get; set; } = "Unknown camera";
    public string CapturedAt { get; set; } = "";
    public double FocalPixels
    {
        get; set;
    }
    public string FocalSource { get; set; } = "assumed 60 degree horizontal field of view";
    public GeoPosition? Gps
    {
        get; set;
    }
    public Attitude? Attitude
    {
        get; set;
    }
    public bool Enabled { get; set; } = true;
    public bool Discarded
    {
        get; set;
    }
    public string? DuplicateOf
    {
        get; set;
    }
    public bool KeepWhenTrimming
    {
        get; set;
    }
    public List<string> Sets { get; set; } = [];
    public Dictionary<string, JsonElement> Metadata { get; set; } = [];
}
