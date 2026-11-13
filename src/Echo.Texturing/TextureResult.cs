using Echo.Contracts;
using System.Text.Json.Serialization;

namespace Echo.Texturing;

public sealed class TextureResult
{
    [JsonIgnore]
    public Surface? Surface
    {
        get; set;
    }
    public string? MeshFile
    {
        get; set;
    }
    public int ChartCount
    {
        get; set;
    }
    public int Size
    {
        get; set;
    }
    public List<double[]> Uvs { get; set; } = [];
    public double CoverageFraction
    {
        get; set;
    }
    public string Layout { get; set; } = "Connected mesh charts, non-overlapping planar UVs, perspective-correct photographic baking and dilated gutters";
}
