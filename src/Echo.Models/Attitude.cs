using System.Text.Json.Serialization;

namespace Echo.Models;

public sealed class Attitude
{
    public double Yaw
    {
        get; set;
    }
    public double Pitch
    {
        get; set;
    }
    public double Roll
    {
        get; set;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source
    {
        get; set;
    }
}
