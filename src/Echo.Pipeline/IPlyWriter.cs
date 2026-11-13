namespace Echo.Pipeline;

public interface IPlyWriter
{
    void Write(string path, double[][] points, int[][]? faces = null);
}
