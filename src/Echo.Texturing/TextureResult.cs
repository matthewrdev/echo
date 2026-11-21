using System.Text.Json.Serialization;
using Echo.Models;
using Echo.Texturing.Rasterization;
using Echo.Texturing.Seams;

namespace Echo.Texturing;

public sealed class TextureResult
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ColourGrade? ColourGrade
    {
        get; set;
    }
    public int MinimumAtlasSize
    {
        get; set;
    }
    public int GutterPixels
    {
        get; set;
    }
    public List<TextureAtlasVariant> Variants { get; set; } = [];
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
    public string UvsFile { get; set; } = "uvs.json";
    [JsonIgnore]
    public IList<double[]> Uvs { get; set; } = [];
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
    public string Layout { get; set; } = "Validated conformal charts with distortion-limited adjacent merging; shared UVs and whole-pixel gutters down to the minimum atlas size; coherent source views; global exposure and local seam correction; antialiased perspective-correct projection; nearest-texel gutters and smooth background padding";
}
