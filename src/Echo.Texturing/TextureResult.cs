using Echo.Contracts;
using System.Text.Json.Serialization;

namespace Echo.Texturing;

public sealed class TextureResult
{
    public int VegetationChartCount
    {
        get; set;
    }
    public double VegetationTextureScale { get; set; } = 1;
    public double TexelsPerWorldUnit
    {
        get; set;
    }
    public double BaselineTexelsPerWorldUnit
    {
        get; set;
    }
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
    [JsonIgnore]
    public TextureSeamCorrections? Corrections
    {
        get; set;
    }
    public int SeamEdges
    {
        get; set;
    }
    public double MeanSeamDifferenceBefore
    {
        get; set;
    }
    public double MeanSeamDifferenceAfter
    {
        get; set;
    }
    public string Layout { get; set; } = "Connected non-overlapping UV charts; coherent source views; global exposure and local seam correction; antialiased perspective-correct projection; nearest-texel gutters and smooth background padding";
}
