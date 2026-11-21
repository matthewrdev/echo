namespace Echo.Models;

public sealed class Surface
{
    public IList<double[]> Vertices { get; set; } = [];
    public IList<int[]> Faces { get; set; } = [];
    // One source per assigned face. Empty entries mark completed regions with
    // no visible photograph; their texture is interpolated from the boundary.
    public IList<string> SourceImages { get; set; } = [];
}
