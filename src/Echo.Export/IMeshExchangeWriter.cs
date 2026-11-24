using Echo.Models;

namespace Echo.Export;

public interface IMeshExchangeWriter
{
    void WriteStl(string path, Surface surface);
    void WriteTextured(string directory, MeshExportFormat format, Surface surface, IList<double[]> uvs, string atlas);
    void WriteGltf(string directory, string glb);
}
