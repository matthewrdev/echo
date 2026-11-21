using Echo.Models;

namespace Echo.Export;

public interface IGisWriter
{
    void Write(string directory, ReconstructionScene scene, Surface? surface, IReadOnlyList<Photo> photos);
}
