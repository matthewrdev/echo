using Echo.Models;

namespace Echo.Export;

public interface IGlbWriter
{
    void Write(string output, Surface mesh, IList<double[]> uvs, byte[] texture);
    object Inspect(string path);
}
