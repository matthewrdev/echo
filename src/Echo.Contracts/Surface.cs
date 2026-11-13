namespace Echo.Contracts;

public sealed class Surface
{
    public List<double[]> Vertices { get; set; } = [];
    public List<int[]> Faces { get; set; } = [];
    public List<string> SourceImages { get; set; } = [];
}
