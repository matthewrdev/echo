using Echo.Contracts;

namespace Echo.Export;

public interface IGlbWriter
{
    void Write(string output, Surface mesh, IReadOnlyList<double[]> uvs, byte[] texture);
    object Inspect(string path);
}
