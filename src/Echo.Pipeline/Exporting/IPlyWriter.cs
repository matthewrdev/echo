namespace Echo.Pipeline.Exporting;

public interface IPlyWriter
{
    void Write(string path, IList<double[]> points, IList<int[]>? faces = null);
}
