using Echo.Models;

namespace Echo.Export;

public interface IPrintMeshWriter
{
    void Write(string path, Surface surface, double sizeMillimetres);
}
