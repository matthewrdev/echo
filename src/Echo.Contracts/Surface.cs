namespace Echo.Contracts;

public sealed class Surface
{
    public List<double[]> Vertices { get; set; } = [];
    public List<int[]> Faces { get; set; } = [];
    // One source per assigned face. Empty entries mark completed regions with
    // no visible photograph; their texture is interpolated from the boundary.
    public List<string> SourceImages { get; set; } = [];
}
