using Echo.Models;

namespace Echo.Export;

public interface IGeoPackageWriter
{
    void Write(string path, ReconstructionScene scene, Surface? surface, IReadOnlyList<Photo> photos);
}
